using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace RevitMCP.Addin.Audit;

internal enum AuditAppendStatus
{
    Durable,
    Failed,
    Rejected
}

internal readonly record struct AuditPreflightResult(bool Succeeded);

internal enum AuditTransactionStatus
{
    None,
    Committed,
    RolledBack,
    Pending,
    Unknown
}

internal enum AuditVerificationStatus
{
    NotRun,
    Passed,
    Failed
}

internal enum AuditApplyStatus
{
    Stale,
    TransactionFailed,
    CommittedUnverified,
    Indeterminate,
    Applied,
    AuditFailed
}

internal sealed class AuditWriterOptions
{
    public string? Directory { get; init; }

    public long AdmissionBytes { get; set; } = 67_108_864;

    public int MaxLineBytes { get; set; } = 8192;

    public int HeadroomBytes { get; set; } = 16_384;

    public TimeSpan Retention { get; init; } = TimeSpan.FromDays(30);

    public bool FailFlush { get; set; }

    public bool FailHousekeeping { get; set; }

    public int? FailAfterWriteBytes { get; set; }
}

internal sealed class AuditCommonMetadata
{
    public required string AttemptRef { get; init; }

    public required string IntentRef { get; init; }

    public required string IntentFingerprint { get; init; }

    public required int IntentFingerprintSchemaVersion { get; init; }

    public required string InstanceId { get; init; }

    public required string DocumentId { get; init; }

    public required string RevitVersion { get; init; }

    public required string RevitBuild { get; init; }

    public required string AddinVersion { get; init; }

    public required int ItemCount { get; init; }
}

internal sealed class AuditEventDraft
{
    public required DateTimeOffset EventAtUtc { get; init; }

    public required string AttemptRef { get; init; }

    public required string IntentRef { get; init; }

    public required string IntentFingerprint { get; init; }

    public required int IntentFingerprintSchemaVersion { get; init; }

    public required string InstanceId { get; init; }

    public required string DocumentId { get; init; }

    public required string RevitVersion { get; init; }

    public required string RevitBuild { get; init; }

    public required string AddinVersion { get; init; }

    public required int ItemCount { get; init; }

    public string? ApprovalMethod { get; init; }

    public DateTimeOffset? ApprovalDecidedAtUtc { get; init; }

    public DateTimeOffset? ApprovalEffectiveExpiryUtc { get; init; }

    public AuditApplyStatus? ApplyStatus { get; init; }

    public AuditTransactionStatus? TransactionStatus { get; init; }

    public AuditVerificationStatus? VerificationStatus { get; init; }
}

internal sealed class ControlledWriteAuditWriter : IDisposable
{
    internal const int SchemaVersion = 1;
    internal const string ApprovalMethodSource = "revit-local-in-process";
    internal const string ApprovalMethodAudit = "revit_local_in_process";

    private readonly object _gate = new();
    private readonly TimeProvider _clock;
    private readonly AuditWriterOptions _options;
    private readonly string _streamRef;
    private FileStream? _stream;
    private DateOnly _streamDate;
    private string? _activePath;
    private long _durableLength;
    private int _durableSequence;
    private bool _degraded;
    private bool _stopped;
    private bool _disposed;

    internal bool InjectStopIoFailure { get; set; }

    public ControlledWriteAuditWriter(TimeProvider? clock = null, AuditWriterOptions? options = null)
    {
        _clock = clock ?? TimeProvider.System;
        _options = options ?? new AuditWriterOptions();
        _streamRef = ToLowerHex(RandomNumberGenerator.GetBytes(32));
    }

    public bool IsDegraded
    {
        get
        {
            lock (_gate)
            {
                return _degraded;
            }
        }
    }

    public string AuditStreamRef => _streamRef;

    public static bool TryMapApprovalMethod(string? providerMethod, out string auditMethod)
    {
        if (string.Equals(providerMethod, ApprovalMethodSource, StringComparison.Ordinal))
        {
            auditMethod = ApprovalMethodAudit;
            return true;
        }

        auditMethod = string.Empty;
        return false;
    }

    public static string HashIntentRef(string raw) => Hash("revitmcp-audit-v1:intent_ref:", raw);

    public static string HashInstanceId(string raw) => Hash("revitmcp-audit-v1:instance_id:", raw);

    public static string HashDocumentId(string raw) => Hash("revitmcp-audit-v1:document_id:", raw);

    public AuditPreflightResult Preflight(AuditCommonMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        lock (_gate)
        {
            if (_stopped)
            {
                return new AuditPreflightResult(false);
            }

            if (!CanRepresent(metadata))
            {
                return new AuditPreflightResult(false);
            }

            try
            {
                if (_degraded && _stream is not null && !RestoreDurableBoundary())
                {
                    _degraded = true;
                    return new AuditPreflightResult(false);
                }

                HousekeepingCore();
                var usage = DirectoryUsage();
                if (usage < 0 || usage > _options.AdmissionBytes - _options.HeadroomBytes)
                {
                    return new AuditPreflightResult(false);
                }

                if (!RotateIfNeeded())
                {
                    _degraded = true;
                    return new AuditPreflightResult(false);
                }

                _degraded = false;
                return new AuditPreflightResult(true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _degraded = true;
                return new AuditPreflightResult(false);
            }
        }
    }

    internal void TryHousekeepingAtProcessStart()
    {
        lock (_gate)
        {
            if (_stopped || _stream is not null)
            {
                return;
            }

            try
            {
                if (_options.FailHousekeeping)
                {
                    throw new UnauthorizedAccessException("injected");
                }

                if (!Directory.Exists(ResolveDirectory()))
                {
                    return;
                }

                HousekeepingCore();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
            }
        }
    }

    public AuditAppendStatus TryAppendApplyStarted(AuditEventDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (!TryMapApprovalMethod(draft.ApprovalMethod, out _)
            || draft.ApprovalDecidedAtUtc is null
            || draft.ApprovalEffectiveExpiryUtc is null
            || draft.TransactionStatus == AuditTransactionStatus.Pending)
        {
            return AuditAppendStatus.Rejected;
        }

        return Append(draft, started: true);
    }

    public AuditAppendStatus TryAppendApplyCompleted(AuditEventDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (draft.ApplyStatus is null
            || draft.TransactionStatus is null
            || draft.VerificationStatus is null
            || draft.TransactionStatus == AuditTransactionStatus.Pending)
        {
            return AuditAppendStatus.Rejected;
        }

        return Append(draft, started: false);
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (_stopped)
            {
                return;
            }

            _stopped = true;
            try
            {
                if (InjectStopIoFailure)
                {
                    throw new IOException("injected");
                }

                if (!RestoreDurableBoundary())
                {
                    _degraded = true;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _degraded = true;
            }

            try
            {
                if (InjectStopIoFailure)
                {
                    throw new IOException("injected");
                }

                _stream?.Dispose();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _degraded = true;
            }

            _stream = null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
    }

    private AuditAppendStatus Append(AuditEventDraft draft, bool started)
    {
        lock (_gate)
        {
            if (_stopped || !ValidateCommon(draft))
            {
                return AuditAppendStatus.Rejected;
            }

            if (_degraded)
            {
                return AuditAppendStatus.Failed;
            }

            try
            {
                if (!EnsureStream() || !RotateIfNeeded())
                {
                    _degraded = true;
                    return AuditAppendStatus.Failed;
                }

                if (_durableSequence == int.MaxValue)
                {
                    return AuditAppendStatus.Rejected;
                }

                var next = _durableSequence + 1;
                if (!TrySerialize(draft, started, next, out var line))
                {
                    return AuditAppendStatus.Rejected;
                }

                var boundary = _durableLength;
                _stream!.Position = boundary;
                WriteCandidate(line);
                if (!ConfirmDurable())
                {
                    _degraded = true;
                    return AuditAppendStatus.Failed;
                }

                _durableLength = boundary + line.Length;
                _durableSequence = next;
                return AuditAppendStatus.Durable;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _degraded = true;
                return AuditAppendStatus.Failed;
            }
        }
    }

    private bool ValidateCommon(AuditEventDraft draft)
    {
        return CanRepresent(
            draft.AttemptRef,
            draft.IntentRef,
            draft.IntentFingerprint,
            draft.IntentFingerprintSchemaVersion,
            draft.InstanceId,
            draft.DocumentId,
            draft.RevitVersion,
            draft.RevitBuild,
            draft.AddinVersion,
            draft.ItemCount);
    }

    private static bool CanRepresent(AuditCommonMetadata metadata)
    {
        return CanRepresent(
            metadata.AttemptRef,
            metadata.IntentRef,
            metadata.IntentFingerprint,
            metadata.IntentFingerprintSchemaVersion,
            metadata.InstanceId,
            metadata.DocumentId,
            metadata.RevitVersion,
            metadata.RevitBuild,
            metadata.AddinVersion,
            metadata.ItemCount);
    }

    private static bool CanRepresent(
        string? attemptRef,
        string? intentRef,
        string? intentFingerprint,
        int schemaVersion,
        string? instanceId,
        string? documentId,
        string? revitVersion,
        string? revitBuild,
        string? addinVersion,
        int itemCount)
    {
        return IsLowerHex(attemptRef, 64)
            && IsLowerHex(intentFingerprint, 64)
            && schemaVersion > 0
            && !string.IsNullOrEmpty(intentRef)
            && !string.IsNullOrEmpty(instanceId)
            && !string.IsNullOrEmpty(documentId)
            && Fits(revitVersion, 1, 32)
            && Fits(revitBuild, 1, 64)
            && Fits(addinVersion, 1, 32)
            && itemCount is >= 1 and <= 20;
    }

    private void WriteCandidate(byte[] line)
    {
        if (_options.FailAfterWriteBytes is int partial)
        {
            var count = Math.Clamp(partial, 0, line.Length);
            if (count > 0)
            {
                _stream!.Write(line, 0, count);
            }

            throw new IOException("injected write failure");
        }

        _stream!.Write(line);
    }

    private bool RestoreDurableBoundary()
    {
        if (_stream is null)
        {
            return true;
        }

        _stream.SetLength(_durableLength);
        _stream.Position = _durableLength;
        return ConfirmDurable();
    }

    private bool TrySerialize(AuditEventDraft draft, bool started, int sequence, out byte[] line)
    {
        line = Array.Empty<byte>();
        var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            Indented = false
        }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schema_version", SchemaVersion);
            writer.WriteString("event_type", started ? "apply_started" : "apply_completed");
            writer.WriteString("event_at_utc", FormatUtc(draft.EventAtUtc));
            writer.WriteString("audit_stream_ref", _streamRef);
            writer.WriteNumber("sequence", sequence);
            writer.WriteString("attempt_ref", draft.AttemptRef);
            writer.WriteString("intent_ref_hash", HashIntentRef(draft.IntentRef));
            writer.WriteString("intent_fingerprint", draft.IntentFingerprint);
            writer.WriteNumber("intent_fingerprint_schema_version", draft.IntentFingerprintSchemaVersion);
            writer.WriteString("instance_id_hash", HashInstanceId(draft.InstanceId));
            writer.WriteString("document_id_hash", HashDocumentId(draft.DocumentId));
            writer.WriteString("revit_version", draft.RevitVersion);
            writer.WriteString("revit_build", draft.RevitBuild);
            writer.WriteString("addin_version", draft.AddinVersion);
            writer.WriteNumber("item_count", draft.ItemCount);
            if (started)
            {
                writer.WriteString("approval_method", ApprovalMethodAudit);
                writer.WriteString("approval_decided_at_utc", FormatUtc(draft.ApprovalDecidedAtUtc!.Value));
                writer.WriteString("approval_effective_expiry_utc", FormatUtc(draft.ApprovalEffectiveExpiryUtc!.Value));
            }
            else
            {
                writer.WriteString("apply_status", Wire(draft.ApplyStatus!.Value));
                writer.WriteString("transaction_status", Wire(draft.TransactionStatus!.Value));
                writer.WriteString("verification_status", Wire(draft.VerificationStatus!.Value));
            }

            writer.WriteEndObject();
        }

        var payload = buffer.ToArray();
        if (payload.Length + 1 > _options.MaxLineBytes)
        {
            return false;
        }

        line = new byte[payload.Length + 1];
        payload.CopyTo(line, 0);
        line[^1] = (byte)'\n';
        return true;
    }

    private bool EnsureStream()
    {
        if (_stream is not null)
        {
            return true;
        }

        var directory = ResolveDirectory();
        Directory.CreateDirectory(directory);
        var today = DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime);
        var path = Path.Combine(directory, FileName(today));
        _stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        _streamDate = today;
        _activePath = path;
        _durableLength = 0;
        _durableSequence = 0;
        return true;
    }

    private bool RotateIfNeeded()
    {
        var today = DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime);
        if (_stream is not null && _streamDate == today)
        {
            return true;
        }

        _stream?.Dispose();
        _stream = null;
        _activePath = null;
        HousekeepingCore();
        return EnsureStream();
    }

    private bool ConfirmDurable()
    {
        if (_stream is null)
        {
            return true;
        }

        if (_options.FailFlush)
        {
            _stream.Flush(flushToDisk: false);
            return false;
        }

        _stream.Flush(flushToDisk: true);
        return true;
    }

    private void HousekeepingCore()
    {
        var directory = ResolveDirectory();
        if (!Directory.Exists(directory))
        {
            return;
        }

        var today = DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime);
        foreach (var path in Directory.EnumerateFiles(directory, "write-*.jsonl"))
        {
            if (_activePath is not null && string.Equals(path, _activePath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var name = Path.GetFileName(path);
            if (!TryReadFileDate(name, out var fileDate))
            {
                continue;
            }

            if (today.DayNumber - fileDate.DayNumber <= _options.Retention.TotalDays)
            {
                continue;
            }

            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private long DirectoryUsage()
    {
        var directory = ResolveDirectory();
        if (!Directory.Exists(directory))
        {
            return 0;
        }

        long total = 0;
        foreach (var path in Directory.EnumerateFiles(directory))
        {
            total += new FileInfo(path).Length;
        }

        return total;
    }

    private string ResolveDirectory()
    {
        if (!string.IsNullOrWhiteSpace(_options.Directory))
        {
            return _options.Directory;
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RevitMCP",
            "Audit",
            "v1");
    }

    private string FileName(DateOnly date)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"write-{date:yyyyMMdd}-{_streamRef}.jsonl");
    }

    private static bool TryReadFileDate(string name, out DateOnly date)
    {
        date = default;
        const string prefix = "write-";
        if (name.Length < prefix.Length + 8 || !name.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        return DateOnly.TryParseExact(name.AsSpan(prefix.Length, 8), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    private static bool Fits(string? value, int minBytes, int maxBytes)
    {
        if (value is null)
        {
            return false;
        }

        var bytes = Encoding.UTF8.GetByteCount(value);
        return bytes >= minBytes && bytes <= maxBytes;
    }

    private static bool IsLowerHex(string? value, int length)
    {
        if (value is null || value.Length != length)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (character is (< '0' or > '9') and (< 'a' or > 'f'))
            {
                return false;
            }
        }

        return true;
    }

    private static string FormatUtc(DateTimeOffset value)
    {
        return value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
    }

    private static string Hash(string prefix, string raw)
    {
        var bytes = Encoding.UTF8.GetBytes(prefix + raw);
        return ToLowerHex(SHA256.HashData(bytes));
    }

    private static string ToLowerHex(ReadOnlySpan<byte> bytes)
    {
        const string alphabet = "0123456789abcdef";
        var chars = new char[bytes.Length * 2];
        for (var index = 0; index < bytes.Length; index++)
        {
            chars[index * 2] = alphabet[bytes[index] >> 4];
            chars[(index * 2) + 1] = alphabet[bytes[index] & 0x0F];
        }

        return new string(chars);
    }

    private static string Wire(AuditApplyStatus status) => status switch
    {
        AuditApplyStatus.Stale => "stale",
        AuditApplyStatus.TransactionFailed => "transaction_failed",
        AuditApplyStatus.CommittedUnverified => "committed_unverified",
        AuditApplyStatus.Indeterminate => "indeterminate",
        AuditApplyStatus.Applied => "applied",
        AuditApplyStatus.AuditFailed => "audit_failed",
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };

    private static string Wire(AuditTransactionStatus status) => status switch
    {
        AuditTransactionStatus.None => "none",
        AuditTransactionStatus.Committed => "committed",
        AuditTransactionStatus.RolledBack => "rolled_back",
        AuditTransactionStatus.Pending => "pending",
        AuditTransactionStatus.Unknown => "unknown",
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };

    private static string Wire(AuditVerificationStatus status) => status switch
    {
        AuditVerificationStatus.NotRun => "not_run",
        AuditVerificationStatus.Passed => "passed",
        AuditVerificationStatus.Failed => "failed",
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };
}
