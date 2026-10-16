using System.Security.Cryptography;
using BimIt.Revit.Addin.Intents;

namespace BimIt.Revit.Addin.Approval;

internal enum ApprovalReviewStatus
{
    Started,
    AlreadyActive,
    Busy,
    Unavailable,
    Terminal
}

internal readonly record struct ApprovalReviewResult(ApprovalReviewStatus Status, string? SessionRef);

internal enum ApprovalCommandStatus
{
    Recorded,
    InvalidSession,
    Unavailable
}

internal readonly record struct ApprovalCommandResult(ApprovalCommandStatus Status);

internal enum ApprovalDismissStatus
{
    Dismissed,
    InvalidSession
}

internal readonly record struct ApprovalDismissResult(ApprovalDismissStatus Status);

internal enum ApprovalConsumeStatus
{
    Consumed,
    NotApproved
}

internal readonly record struct ApprovalSnapshot(
    string IntentRef,
    string IntentFingerprint,
    string InstanceId,
    string DocumentId,
    DateTimeOffset DecisionTimestamp,
    string ProviderMethod,
    DateTimeOffset EffectiveExpiry,
    ControlledWriteIntentKind IntentKind = ControlledWriteIntentKind.ParameterUpdates);

internal readonly record struct ApprovalConsumeResult(ApprovalConsumeStatus Status, ApprovalSnapshot? Snapshot);

internal enum ApprovalObservation
{
    None,
    Retained,
    Ended
}

/// <summary>
/// In-process approval session and decision state. Not wired to Revit, UI, Bridge, or MCP.
/// </summary>
internal sealed class RevitLocalApprovalProviderStateMachine
{
    internal const string DefaultProviderMethod = "revit-local-in-process";

    private readonly object _gate = new();
    private readonly EphemeralWriteIntentStore _intents;
    private readonly TimeProvider _clock;
    private readonly Func<string> _sessionRefFactory;
    private readonly string _providerMethod;
    private readonly Dictionary<string, TerminalDecision> _decisions = new(StringComparer.Ordinal);
    private ActiveSession? _session;
    private int _sessionSequence;
    private bool _stopped;

    public RevitLocalApprovalProviderStateMachine(
        EphemeralWriteIntentStore intents,
        TimeProvider? clock = null,
        Func<string>? sessionRefFactory = null,
        string? providerMethod = null)
    {
        ArgumentNullException.ThrowIfNull(intents);
        _intents = intents;
        _clock = clock ?? TimeProvider.System;
        _sessionRefFactory = sessionRefFactory ?? CreateSessionRef;
        _providerMethod = string.IsNullOrWhiteSpace(providerMethod) ? DefaultProviderMethod : providerMethod;
    }

    public ApprovalReviewResult BeginReview(string? intentRef, string? activeDocumentId)
    {
        return BeginReview(intentRef, activeDocumentId, ControlledWriteIntentKind.ParameterUpdates);
    }

    public ApprovalReviewResult BeginReview(string? intentRef, string? activeDocumentId, ControlledWriteIntentKind expectedKind)
    {
        lock (_gate)
        {
            if (_stopped)
            {
                return UnavailableReview();
            }

            InvalidateStaleSession(activeDocumentId);
            if (!TryExpected(intentRef, expectedKind, out var binding) || !IsRequestedDocument(activeDocumentId, binding))
            {
                return UnavailableReview();
            }

            if (TryMatchingDecision(binding, out _))
            {
                return new ApprovalReviewResult(ApprovalReviewStatus.Terminal, null);
            }

            if (_session is not null)
            {
                if (SameIntent(_session, binding))
                {
                    return new ApprovalReviewResult(ApprovalReviewStatus.AlreadyActive, _session.SessionRef);
                }

                return new ApprovalReviewResult(ApprovalReviewStatus.Busy, null);
            }

            if (!TryCreateSessionRef(out var sessionRef))
            {
                return UnavailableReview();
            }

            _session = new ActiveSession(sessionRef, binding);
            return new ApprovalReviewResult(ApprovalReviewStatus.Started, sessionRef);
        }
    }

    public ApprovalObservation ObserveActiveDocument(string? activeDocumentId)
    {
        lock (_gate)
        {
            if (_stopped || _session is null)
            {
                return ApprovalObservation.None;
            }

            if (!SameDocument(activeDocumentId, _session.DocumentId) || !SessionStillBound(_session))
            {
                _session = null;
                return ApprovalObservation.Ended;
            }

            return ApprovalObservation.Retained;
        }
    }

    public ApprovalCommandResult ApproveCurrent(string? sessionRef, string? activeDocumentId)
    {
        return Decide(sessionRef, activeDocumentId, approved: true);
    }

    public ApprovalCommandResult RejectCurrent(string? sessionRef, string? activeDocumentId)
    {
        return Decide(sessionRef, activeDocumentId, approved: false);
    }

    public ApprovalDismissResult DismissCurrent(string? sessionRef)
    {
        lock (_gate)
        {
            if (_session is null || !string.Equals(_session.SessionRef, sessionRef, StringComparison.Ordinal))
            {
                return new ApprovalDismissResult(ApprovalDismissStatus.InvalidSession);
            }

            _session = null;
            return new ApprovalDismissResult(ApprovalDismissStatus.Dismissed);
        }
    }

    public ApprovalConsumeResult TryConsumeApproved(string? intentRef)
    {
        return TryConsumeApproved(intentRef, ControlledWriteIntentKind.ParameterUpdates);
    }

    public ApprovalConsumeResult TryConsumeApproved(string? intentRef, ControlledWriteIntentKind expectedKind)
    {
        lock (_gate)
        {
            if (_stopped || !TryExpected(intentRef, expectedKind, out var binding) || !TryMatchingDecision(binding, out var decision) || decision is null)
            {
                return NotApproved();
            }

            if (!decision.Approved || decision.Consumed || decision.Kind != expectedKind)
            {
                return NotApproved();
            }

            decision.Consumed = true;
            return new ApprovalConsumeResult(
                ApprovalConsumeStatus.Consumed,
                new ApprovalSnapshot(
                    decision.IntentRef,
                    decision.Fingerprint,
                    decision.InstanceId,
                    decision.DocumentId,
                    decision.DecidedAt,
                    decision.ProviderMethod,
                    decision.EffectiveExpiry,
                    decision.Kind));
        }
    }

    public int ForgetDocument(string documentId)
    {
        ArgumentNullException.ThrowIfNull(documentId);
        lock (_gate)
        {
            var removed = 0;
            if (_session is not null && string.Equals(_session.DocumentId, documentId, StringComparison.Ordinal))
            {
                _session = null;
                removed++;
            }

            var keys = new List<string>();
            foreach (var pair in _decisions)
            {
                if (string.Equals(pair.Value.DocumentId, documentId, StringComparison.Ordinal))
                {
                    keys.Add(pair.Key);
                }
            }

            foreach (var key in keys)
            {
                _decisions.Remove(key);
                removed++;
            }

            return removed;
        }
    }

    public int PurgeExpired()
    {
        lock (_gate)
        {
            var removed = 0;
            if (_session is not null && !SessionStillBound(_session))
            {
                _session = null;
                removed++;
            }

            var keys = new List<string>();
            foreach (var pair in _decisions)
            {
                if (!DecisionStillBound(pair.Value))
                {
                    keys.Add(pair.Key);
                }
            }

            foreach (var key in keys)
            {
                _decisions.Remove(key);
                removed++;
            }

            return removed;
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _session = null;
            _decisions.Clear();
            _stopped = true;
        }
    }

    private ApprovalCommandResult Decide(string? sessionRef, string? activeDocumentId, bool approved)
    {
        lock (_gate)
        {
            if (_stopped)
            {
                return new ApprovalCommandResult(ApprovalCommandStatus.Unavailable);
            }

            if (_session is null || !string.Equals(_session.SessionRef, sessionRef, StringComparison.Ordinal))
            {
                return new ApprovalCommandResult(ApprovalCommandStatus.InvalidSession);
            }

            if (!TryExpected(_session.IntentRef, _session.Kind, out var binding) || !SessionMatches(binding, _session) || !SameDocument(activeDocumentId, _session.DocumentId))
            {
                _session = null;
                return new ApprovalCommandResult(ApprovalCommandStatus.Unavailable);
            }

            if (_decisions.TryGetValue(binding.IntentRef, out var existing))
            {
                _session = null;
                if (!Matches(binding, existing))
                {
                    _decisions.Remove(binding.IntentRef);
                    return new ApprovalCommandResult(ApprovalCommandStatus.Unavailable);
                }

                if (existing.Approved == approved)
                {
                    return new ApprovalCommandResult(ApprovalCommandStatus.Recorded);
                }

                return new ApprovalCommandResult(ApprovalCommandStatus.Unavailable);
            }

            _decisions.Add(
                binding.IntentRef,
                new TerminalDecision(
                    binding,
                    approved,
                    _clock.GetUtcNow(),
                    _providerMethod));
            _session = null;
            return new ApprovalCommandResult(ApprovalCommandStatus.Recorded);
        }
    }

    private void InvalidateStaleSession(string? activeDocumentId)
    {
        if (_session is null)
        {
            return;
        }

        if (!SameDocument(activeDocumentId, _session.DocumentId) || !SessionStillBound(_session))
        {
            _session = null;
        }
    }

    private bool TryMatchingDecision(ControlledWriteIntentBinding binding, out TerminalDecision? decision)
    {
        decision = null;
        if (!_decisions.TryGetValue(binding.IntentRef, out var stored))
        {
            return false;
        }

        if (Matches(binding, stored))
        {
            decision = stored;
            return true;
        }

        _decisions.Remove(binding.IntentRef);
        return false;
    }

    private bool SessionStillBound(ActiveSession session)
    {
        return TryExpected(session.IntentRef, session.Kind, out var binding) && SessionMatches(binding, session);
    }

    private bool DecisionStillBound(TerminalDecision decision)
    {
        return TryExpected(decision.IntentRef, decision.Kind, out var binding) && Matches(binding, decision);
    }

    private bool TryExpected(string? intentRef, ControlledWriteIntentKind expectedKind, out ControlledWriteIntentBinding binding)
    {
        binding = null!;
        if (intentRef is null || !_intents.TryGetControlled(intentRef, out var stored) || stored is null)
        {
            return false;
        }

        if (stored.Binding.Kind != expectedKind)
        {
            return false;
        }

        binding = stored.Binding;
        return true;
    }

    private bool TryCreateSessionRef(out string sessionRef)
    {
        sessionRef = string.Empty;
        string created;
        try
        {
            created = _sessionRefFactory();
        }
        catch (Exception)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(created) || _sessionSequence == int.MaxValue)
        {
            return false;
        }

        _sessionSequence++;
        sessionRef = string.Concat(created, "~", _sessionSequence.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return true;
    }

    private static bool IsRequestedDocument(string? activeDocumentId, ControlledWriteIntentBinding binding)
    {
        return SameDocument(activeDocumentId, binding.DocumentId);
    }

    private static bool SameIntent(ActiveSession session, ControlledWriteIntentBinding binding)
    {
        return SessionMatches(binding, session);
    }

    private static bool SessionMatches(ControlledWriteIntentBinding binding, ActiveSession session)
    {
        return binding.Kind == session.Kind
            && string.Equals(binding.IntentRef, session.IntentRef, StringComparison.Ordinal)
            && string.Equals(binding.IntentFingerprint, session.Fingerprint, StringComparison.Ordinal)
            && string.Equals(binding.InstanceId, session.InstanceId, StringComparison.Ordinal)
            && string.Equals(binding.DocumentId, session.DocumentId, StringComparison.Ordinal);
    }

    private static bool Matches(ControlledWriteIntentBinding binding, TerminalDecision decision)
    {
        return binding.Kind == decision.Kind
            && string.Equals(binding.IntentRef, decision.IntentRef, StringComparison.Ordinal)
            && string.Equals(binding.IntentFingerprint, decision.Fingerprint, StringComparison.Ordinal)
            && string.Equals(binding.InstanceId, decision.InstanceId, StringComparison.Ordinal)
            && string.Equals(binding.DocumentId, decision.DocumentId, StringComparison.Ordinal);
    }

    private static bool SameDocument(string? activeDocumentId, string documentId)
    {
        return !string.IsNullOrEmpty(activeDocumentId)
            && string.Equals(activeDocumentId, documentId, StringComparison.Ordinal);
    }

    private static ApprovalReviewResult UnavailableReview()
    {
        return new ApprovalReviewResult(ApprovalReviewStatus.Unavailable, null);
    }

    private static ApprovalConsumeResult NotApproved()
    {
        return new ApprovalConsumeResult(ApprovalConsumeStatus.NotApproved, null);
    }

    private static string CreateSessionRef()
    {
        return Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
    }

    private sealed class ActiveSession
    {
        public ActiveSession(string sessionRef, ControlledWriteIntentBinding binding)
        {
            SessionRef = sessionRef;
            Kind = binding.Kind;
            IntentRef = binding.IntentRef;
            Fingerprint = binding.IntentFingerprint;
            InstanceId = binding.InstanceId;
            DocumentId = binding.DocumentId;
        }

        public string SessionRef { get; }

        public ControlledWriteIntentKind Kind { get; }

        public string IntentRef { get; }

        public string Fingerprint { get; }

        public string InstanceId { get; }

        public string DocumentId { get; }
    }

    private sealed class TerminalDecision
    {
        public TerminalDecision(
            ControlledWriteIntentBinding binding,
            bool approved,
            DateTimeOffset decidedAt,
            string providerMethod)
        {
            Kind = binding.Kind;
            IntentRef = binding.IntentRef;
            Fingerprint = binding.IntentFingerprint;
            InstanceId = binding.InstanceId;
            DocumentId = binding.DocumentId;
            Approved = approved;
            DecidedAt = decidedAt;
            ProviderMethod = providerMethod;
            EffectiveExpiry = binding.ExpiresAt;
        }

        public ControlledWriteIntentKind Kind { get; }

        public string IntentRef { get; }

        public string Fingerprint { get; }

        public string InstanceId { get; }

        public string DocumentId { get; }

        public bool Approved { get; }

        public DateTimeOffset DecidedAt { get; }

        public string ProviderMethod { get; }

        public DateTimeOffset EffectiveExpiry { get; }

        public bool Consumed { get; set; }
    }
}
