using System.Security.Cryptography;

namespace RevitMCP.Addin.Intents;

internal enum IntentStoreCreateStatus
{
    Created,
    CapacityReached,
    Rejected
}

internal readonly record struct IntentCreateResult(
    IntentStoreCreateStatus Status,
    string? IntentRef,
    string? IntentFingerprint,
    DateTimeOffset? ExpiresAt);

internal sealed class EphemeralWriteIntentStore
{
    internal const int LiveCapacity = 64;
    internal const int MaxRandomDraws = 8;
    internal static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    private readonly object _gate = new();
    private readonly Dictionary<string, StoredIntent> _entries = new(StringComparer.Ordinal);
    private readonly TimeProvider _clock;
    private readonly Func<byte[]> _randomBytes;
    private bool _closed;

    public EphemeralWriteIntentStore(TimeProvider? clock = null, Func<byte[]>? randomBytes = null)
    {
        _clock = clock ?? TimeProvider.System;
        _randomBytes = randomBytes ?? (() => RandomNumberGenerator.GetBytes(32));
    }

    public IntentCreateResult TryCreate(IntentDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (!IntentCanonicalEncoder.TryEncode(
                draft,
                out var instanceId,
                out var documentId,
                out var items,
                out _,
                out var fingerprint))
        {
            return new IntentCreateResult(IntentStoreCreateStatus.Rejected, null, null, null);
        }

        lock (_gate)
        {
            if (_closed || _clock.TimestampFrequency <= 0)
            {
                return new IntentCreateResult(IntentStoreCreateStatus.Rejected, null, null, null);
            }

            PurgeExpiredCore();
            if (_entries.Count >= LiveCapacity)
            {
                return new IntentCreateResult(IntentStoreCreateStatus.CapacityReached, null, null, null);
            }

            if (!TryAllocateRef(out var intentRef))
            {
                return new IntentCreateResult(IntentStoreCreateStatus.Rejected, null, null, null);
            }

            var createdAt = _clock.GetUtcNow();
            var expiresAt = createdAt + Lifetime;
            var stored = new StoredIntent(
                intentRef,
                fingerprint,
                createdAt,
                expiresAt,
                _clock.GetTimestamp(),
                instanceId,
                documentId,
                items);
            _entries.Add(intentRef, stored);
            return new IntentCreateResult(IntentStoreCreateStatus.Created, intentRef, fingerprint, expiresAt);
        }
    }

    public bool TryGet(string intentRef, out IntentEntry? entry)
    {
        entry = null;
        if (intentRef is null)
        {
            return false;
        }

        lock (_gate)
        {
            if (!_entries.TryGetValue(intentRef, out var stored) || IsExpired(stored.MonotonicOrigin))
            {
                return false;
            }

            entry = stored.ToEntry();
            return true;
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
            _closed = true;
            return removed;
        }
    }

    private int PurgeExpiredCore()
    {
        var removed = new List<string>();
        foreach (var pair in _entries)
        {
            if (IsExpired(pair.Value.MonotonicOrigin))
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

    private bool TryAllocateRef(out string intentRef)
    {
        intentRef = string.Empty;
        for (var draw = 0; draw < MaxRandomDraws; draw++)
        {
            var bytes = _randomBytes();
            if (bytes is null || bytes.Length != 32)
            {
                continue;
            }

            var candidate = IntentCanonicalEncoder.ToLowerHex(bytes);
            if (!_entries.ContainsKey(candidate))
            {
                intentRef = candidate;
                return true;
            }
        }

        return false;
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

            return elapsedTicks >= Lifetime.Ticks;
        }
        catch (OverflowException)
        {
            return true;
        }
    }

    private sealed class StoredIntent
    {
        public StoredIntent(
            string intentRef,
            string fingerprint,
            DateTimeOffset createdAt,
            DateTimeOffset expiresAt,
            long monotonicOrigin,
            string instanceId,
            string documentId,
            IReadOnlyList<IntentItemEntry> items)
        {
            IntentRef = intentRef;
            Fingerprint = fingerprint;
            CreatedAt = createdAt;
            ExpiresAt = expiresAt;
            MonotonicOrigin = monotonicOrigin;
            InstanceId = instanceId;
            DocumentId = documentId;
            Items = items;
        }

        public string IntentRef { get; }

        public string Fingerprint { get; }

        public DateTimeOffset CreatedAt { get; }

        public DateTimeOffset ExpiresAt { get; }

        public long MonotonicOrigin { get; }

        public string InstanceId { get; }

        public string DocumentId { get; }

        public IReadOnlyList<IntentItemEntry> Items { get; }

        public IntentEntry ToEntry()
        {
            return new IntentEntry(
                IntentRef,
                Fingerprint,
                CreatedAt,
                ExpiresAt,
                InstanceId,
                DocumentId,
                Items);
        }
    }
}
