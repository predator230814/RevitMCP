using System.Security.Cryptography;
using RevitMCP.Addin.Intents;

namespace RevitMCP.Addin.Approval;

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
    DateTimeOffset EffectiveExpiry);

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
        lock (_gate)
        {
            if (_stopped)
            {
                return UnavailableReview();
            }

            InvalidateStaleSession(activeDocumentId);
            if (!TryLive(intentRef, out var entry) || !IsRequestedDocument(activeDocumentId, entry))
            {
                return UnavailableReview();
            }

            if (TryMatchingDecision(entry, out _))
            {
                return new ApprovalReviewResult(ApprovalReviewStatus.Terminal, null);
            }

            if (_session is not null)
            {
                if (SameIntent(_session, entry))
                {
                    return new ApprovalReviewResult(ApprovalReviewStatus.AlreadyActive, _session.SessionRef);
                }

                return new ApprovalReviewResult(ApprovalReviewStatus.Busy, null);
            }

            if (!TryCreateSessionRef(out var sessionRef))
            {
                return UnavailableReview();
            }

            _session = new ActiveSession(sessionRef, entry.IntentRef, entry.IntentFingerprint, entry.InstanceId, entry.DocumentId);
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

            if (!SameDocument(activeDocumentId, _session.DocumentId))
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
        lock (_gate)
        {
            if (_stopped || !TryLive(intentRef, out var entry) || !TryMatchingDecision(entry, out var decision) || decision is null)
            {
                return NotApproved();
            }

            if (!decision.Approved || decision.Consumed)
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
                    decision.EffectiveExpiry));
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

            if (!TryLive(_session.IntentRef, out var entry) || !SessionMatches(entry, _session) || !SameDocument(activeDocumentId, _session.DocumentId))
            {
                _session = null;
                return new ApprovalCommandResult(ApprovalCommandStatus.Unavailable);
            }

            if (_decisions.TryGetValue(entry.IntentRef, out var existing))
            {
                _session = null;
                if (!Matches(entry, existing))
                {
                    _decisions.Remove(entry.IntentRef);
                    return new ApprovalCommandResult(ApprovalCommandStatus.Unavailable);
                }

                if (existing.Approved == approved)
                {
                    return new ApprovalCommandResult(ApprovalCommandStatus.Recorded);
                }

                return new ApprovalCommandResult(ApprovalCommandStatus.Unavailable);
            }

            _decisions.Add(
                entry.IntentRef,
                new TerminalDecision(
                    entry.IntentRef,
                    entry.IntentFingerprint,
                    entry.InstanceId,
                    entry.DocumentId,
                    approved,
                    _clock.GetUtcNow(),
                    _providerMethod,
                    entry.ExpiresAt));
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

    private bool TryMatchingDecision(IntentEntry entry, out TerminalDecision? decision)
    {
        decision = null;
        if (!_decisions.TryGetValue(entry.IntentRef, out var stored))
        {
            return false;
        }

        if (Matches(entry, stored))
        {
            decision = stored;
            return true;
        }

        _decisions.Remove(entry.IntentRef);
        return false;
    }

    private bool SessionStillBound(ActiveSession session)
    {
        return TryLive(session.IntentRef, out var entry) && SessionMatches(entry, session);
    }

    private bool DecisionStillBound(TerminalDecision decision)
    {
        return TryLive(decision.IntentRef, out var entry) && Matches(entry, decision);
    }

    private bool TryLive(string? intentRef, out IntentEntry entry)
    {
        entry = null!;
        if (intentRef is null || !_intents.TryGet(intentRef, out var stored) || stored is null)
        {
            return false;
        }

        entry = stored;
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

        if (string.IsNullOrWhiteSpace(created))
        {
            return false;
        }

        sessionRef = created;
        return true;
    }

    private static bool IsRequestedDocument(string? activeDocumentId, IntentEntry entry)
    {
        return SameDocument(activeDocumentId, entry.DocumentId);
    }

    private static bool SameIntent(ActiveSession session, IntentEntry entry)
    {
        return SessionMatches(entry, session);
    }

    private static bool SessionMatches(IntentEntry entry, ActiveSession session)
    {
        return string.Equals(entry.IntentRef, session.IntentRef, StringComparison.Ordinal)
            && string.Equals(entry.IntentFingerprint, session.Fingerprint, StringComparison.Ordinal)
            && string.Equals(entry.InstanceId, session.InstanceId, StringComparison.Ordinal)
            && string.Equals(entry.DocumentId, session.DocumentId, StringComparison.Ordinal);
    }

    private static bool Matches(IntentEntry entry, TerminalDecision decision)
    {
        return string.Equals(entry.IntentRef, decision.IntentRef, StringComparison.Ordinal)
            && string.Equals(entry.IntentFingerprint, decision.Fingerprint, StringComparison.Ordinal)
            && string.Equals(entry.InstanceId, decision.InstanceId, StringComparison.Ordinal)
            && string.Equals(entry.DocumentId, decision.DocumentId, StringComparison.Ordinal);
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
        public ActiveSession(string sessionRef, string intentRef, string fingerprint, string instanceId, string documentId)
        {
            SessionRef = sessionRef;
            IntentRef = intentRef;
            Fingerprint = fingerprint;
            InstanceId = instanceId;
            DocumentId = documentId;
        }

        public string SessionRef { get; }

        public string IntentRef { get; }

        public string Fingerprint { get; }

        public string InstanceId { get; }

        public string DocumentId { get; }
    }

    private sealed class TerminalDecision
    {
        public TerminalDecision(
            string intentRef,
            string fingerprint,
            string instanceId,
            string documentId,
            bool approved,
            DateTimeOffset decidedAt,
            string providerMethod,
            DateTimeOffset effectiveExpiry)
        {
            IntentRef = intentRef;
            Fingerprint = fingerprint;
            InstanceId = instanceId;
            DocumentId = documentId;
            Approved = approved;
            DecidedAt = decidedAt;
            ProviderMethod = providerMethod;
            EffectiveExpiry = effectiveExpiry;
        }

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
