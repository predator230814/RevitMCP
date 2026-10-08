namespace RevitMCP.Addin.Apply;

internal enum ApplyAttemptState
{
    PreMutation,
    Irrevocable,
    Terminal
}

internal enum ApplyTerminalStatus
{
    Applied,
    Stale,
    TransactionFailed,
    CommittedUnverified,
    Indeterminate,
    AuditFailed
}

internal readonly record struct ApplyBinding(string IntentFingerprint, string InstanceId, string DocumentId);

internal enum ApplyLookupStatus
{
    Absent,
    InProgress,
    Terminal,
    BindingMismatch
}

internal readonly record struct ApplyLookupResult(ApplyLookupStatus Status, ApplyTerminalStatus? Terminal);

internal enum ApplyEstablishStatus
{
    Owner,
    InProgress,
    Exhausted,
    Rejected
}

internal enum ApplyReleaseStatus
{
    Released,
    Rejected
}

internal enum ApplyMarkStatus
{
    Irrevocable,
    Absent
}

internal enum ApplyCompleteStatus
{
    Stored,
    Rejected
}

internal readonly record struct ApplyAttemptLease(string IntentRef, long Token)
{
    public static ApplyAttemptLease None => default;

    public bool IsAssigned => Token != 0 && IntentRef is not null;
}

internal sealed class ControlledApplyAttemptStore
{
    internal const int LiveCapacity = 128;
    internal static readonly TimeSpan TerminalLifetime = TimeSpan.FromMinutes(30);

    private readonly object _gate = new();
    private readonly Dictionary<string, Attempt> _entries = new(StringComparer.Ordinal);
    private readonly TimeProvider _clock;
    private long _nextToken = 1;
    private bool _stopped;

    public ControlledApplyAttemptStore(TimeProvider? clock = null)
    {
        _clock = clock ?? TimeProvider.System;
    }

    public ApplyLookupResult TryLookup(string intentRef, ApplyBinding? liveBinding)
    {
        ArgumentNullException.ThrowIfNull(intentRef);
        lock (_gate)
        {
            if (!TryGetLive(intentRef, out var attempt))
            {
                return new ApplyLookupResult(ApplyLookupStatus.Absent, null);
            }

            if (liveBinding is { } binding && !SameBinding(attempt, binding))
            {
                return new ApplyLookupResult(ApplyLookupStatus.BindingMismatch, null);
            }

            if (attempt.State == ApplyAttemptState.Terminal)
            {
                return new ApplyLookupResult(ApplyLookupStatus.Terminal, attempt.Terminal);
            }

            return new ApplyLookupResult(ApplyLookupStatus.InProgress, null);
        }
    }

    public ApplyEstablishStatus TryEstablishExclusive(
        string intentRef,
        ApplyBinding binding,
        out ApplyAttemptLease lease)
    {
        ArgumentNullException.ThrowIfNull(intentRef);
        ArgumentNullException.ThrowIfNull(binding.IntentFingerprint);
        ArgumentNullException.ThrowIfNull(binding.InstanceId);
        ArgumentNullException.ThrowIfNull(binding.DocumentId);
        lease = ApplyAttemptLease.None;
        lock (_gate)
        {
            if (_stopped || _clock.TimestampFrequency <= 0)
            {
                return ApplyEstablishStatus.Rejected;
            }

            if (TryGetLive(intentRef, out _))
            {
                return ApplyEstablishStatus.InProgress;
            }

            PurgeExpiredCore();
            if (_entries.Count >= LiveCapacity)
            {
                return ApplyEstablishStatus.Exhausted;
            }

            var token = _nextToken++;
            _entries[intentRef] = new Attempt(intentRef, binding, token);
            lease = new ApplyAttemptLease(intentRef, token);
            return ApplyEstablishStatus.Owner;
        }
    }

    public ApplyReleaseStatus ReleasePreMutation(ApplyAttemptLease lease)
    {
        lock (_gate)
        {
            if (!TryOwned(lease, out var attempt) || attempt.State != ApplyAttemptState.PreMutation)
            {
                return ApplyReleaseStatus.Rejected;
            }

            _entries.Remove(lease.IntentRef);
            return ApplyReleaseStatus.Released;
        }
    }

    public ApplyMarkStatus MarkIrrevocable(ApplyAttemptLease lease)
    {
        lock (_gate)
        {
            if (!TryOwned(lease, out var attempt) || attempt.State == ApplyAttemptState.Terminal)
            {
                return ApplyMarkStatus.Absent;
            }

            attempt.State = ApplyAttemptState.Irrevocable;
            return ApplyMarkStatus.Irrevocable;
        }
    }

    public ApplyCompleteStatus Complete(ApplyAttemptLease lease, ApplyTerminalStatus status)
    {
        lock (_gate)
        {
            if (!TryOwned(lease, out var attempt) || attempt.State != ApplyAttemptState.Irrevocable)
            {
                return ApplyCompleteStatus.Rejected;
            }

            attempt.State = ApplyAttemptState.Terminal;
            attempt.Terminal = status;
            attempt.MonotonicOrigin = _clock.GetTimestamp();
            return ApplyCompleteStatus.Stored;
        }
    }

    public int ForgetDocument(string documentId)
    {
        ArgumentNullException.ThrowIfNull(documentId);
        lock (_gate)
        {
            var removed = new List<string>();
            foreach (var pair in _entries)
            {
                if (string.Equals(pair.Value.DocumentId, documentId, StringComparison.Ordinal))
                {
                    removed.Add(pair.Key);
                }
            }

            foreach (var key in removed)
            {
                _entries.Remove(key);
            }

            return removed.Count;
        }
    }

    public int PurgeExpired()
    {
        lock (_gate)
        {
            return PurgeExpiredCore();
        }
    }

    public int Clear()
    {
        lock (_gate)
        {
            var removed = _entries.Count;
            _entries.Clear();
            _stopped = true;
            return removed;
        }
    }

    internal bool TryInspect(string intentRef, out ApplyAttemptInspection inspection)
    {
        ArgumentNullException.ThrowIfNull(intentRef);
        lock (_gate)
        {
            if (!_entries.TryGetValue(intentRef, out var attempt))
            {
                inspection = default;
                return false;
            }

            inspection = new ApplyAttemptInspection(
                attempt.IntentRef,
                attempt.Fingerprint,
                attempt.InstanceId,
                attempt.DocumentId,
                attempt.State,
                attempt.Terminal);
            return true;
        }
    }

    private bool TryGetLive(string intentRef, out Attempt attempt)
    {
        if (!_entries.TryGetValue(intentRef, out attempt!))
        {
            return false;
        }

        if (attempt.State == ApplyAttemptState.Terminal && IsExpired(attempt.MonotonicOrigin))
        {
            _entries.Remove(intentRef);
            attempt = null!;
            return false;
        }

        return true;
    }

    private bool TryOwned(ApplyAttemptLease lease, out Attempt attempt)
    {
        attempt = null!;
        if (!lease.IsAssigned || !_entries.TryGetValue(lease.IntentRef, out attempt!))
        {
            return false;
        }

        return attempt.Token == lease.Token;
    }

    private int PurgeExpiredCore()
    {
        var removed = new List<string>();
        foreach (var pair in _entries)
        {
            if (pair.Value.State == ApplyAttemptState.Terminal && IsExpired(pair.Value.MonotonicOrigin))
            {
                removed.Add(pair.Key);
            }
        }

        foreach (var key in removed)
        {
            _entries.Remove(key);
        }

        return removed.Count;
    }

    private static bool SameBinding(Attempt attempt, ApplyBinding binding)
    {
        return string.Equals(attempt.Fingerprint, binding.IntentFingerprint, StringComparison.Ordinal)
            && string.Equals(attempt.InstanceId, binding.InstanceId, StringComparison.Ordinal)
            && string.Equals(attempt.DocumentId, binding.DocumentId, StringComparison.Ordinal);
    }

    private bool IsExpired(long origin)
    {
        var now = _clock.GetTimestamp();
        if (now < origin)
        {
            return false;
        }

        var frequency = _clock.TimestampFrequency;
        if (frequency <= 0)
        {
            return true;
        }

        try
        {
            long elapsedTicks;
            checked
            {
                elapsedTicks = (now - origin) * TimeSpan.TicksPerSecond / frequency;
            }

            return elapsedTicks >= TerminalLifetime.Ticks;
        }
        catch (OverflowException)
        {
            return true;
        }
    }

    private sealed class Attempt
    {
        public Attempt(string intentRef, ApplyBinding binding, long token)
        {
            IntentRef = intentRef;
            Fingerprint = binding.IntentFingerprint;
            InstanceId = binding.InstanceId;
            DocumentId = binding.DocumentId;
            Token = token;
            State = ApplyAttemptState.PreMutation;
        }

        public string IntentRef { get; }

        public string Fingerprint { get; }

        public string InstanceId { get; }

        public string DocumentId { get; }

        public long Token { get; }

        public ApplyAttemptState State { get; set; }

        public ApplyTerminalStatus? Terminal { get; set; }

        public long MonotonicOrigin { get; set; }
    }
}

internal readonly record struct ApplyAttemptInspection(
    string IntentRef,
    string IntentFingerprint,
    string InstanceId,
    string DocumentId,
    ApplyAttemptState State,
    ApplyTerminalStatus? Terminal);
