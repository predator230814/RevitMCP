using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RevitMCP.Addin.Audit;
using RevitMCP.Addin.Intents;
using RevitMCP.Addin.Lifecycle;
using RevitMCP.Contracts;
using Xunit;

namespace RevitMCP.Addin.Tests;

public sealed class ControlledWriteAuditWriterTests
{
    private static readonly string[] StartedFields =
    {
        "schema_version",
        "event_type",
        "event_at_utc",
        "audit_stream_ref",
        "sequence",
        "attempt_ref",
        "intent_ref_hash",
        "intent_fingerprint",
        "intent_fingerprint_schema_version",
        "instance_id_hash",
        "document_id_hash",
        "revit_version",
        "revit_build",
        "addin_version",
        "item_count",
        "approval_method",
        "approval_decided_at_utc",
        "approval_effective_expiry_utc"
    };

    private static readonly string[] CompletedFields =
    {
        "schema_version",
        "event_type",
        "event_at_utc",
        "audit_stream_ref",
        "sequence",
        "attempt_ref",
        "intent_ref_hash",
        "intent_fingerprint",
        "intent_fingerprint_schema_version",
        "instance_id_hash",
        "document_id_hash",
        "revit_version",
        "revit_build",
        "addin_version",
        "item_count",
        "apply_status",
        "transaction_status",
        "verification_status"
    };

    [Fact]
    public void Started_and_completed_lines_use_the_closed_schema()
    {
        using var sandbox = new Sandbox();
        using var writer = sandbox.Open();
        Assert.Equal(AuditAppendStatus.Durable, writer.TryAppendApplyStarted(Started()));
        Assert.Equal(AuditAppendStatus.Durable, writer.TryAppendApplyCompleted(Completed()));
        writer.Stop();

        var lines = Lines(sandbox.Directory);
        Assert.Equal(2, lines.Count);
        var started = JsonDocument.Parse(lines[0]).RootElement;
        var completed = JsonDocument.Parse(lines[1]).RootElement;
        Assert.Equal(StartedFields, started.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.Equal(CompletedFields, completed.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.Equal(1, started.GetProperty("schema_version").GetInt32());
        Assert.Equal("apply_started", started.GetProperty("event_type").GetString());
        Assert.Equal("apply_completed", completed.GetProperty("event_type").GetString());
        Assert.Equal("2026-10-07T15:04:05.1234567Z", started.GetProperty("event_at_utc").GetString());
        Assert.Equal(1, started.GetProperty("sequence").GetInt32());
        Assert.Equal(2, completed.GetProperty("sequence").GetInt32());
        Assert.Equal(ControlledWriteAuditWriter.ApprovalMethodAudit, started.GetProperty("approval_method").GetString());
        Assert.Equal("applied", completed.GetProperty("apply_status").GetString());
        Assert.Equal("committed", completed.GetProperty("transaction_status").GetString());
        Assert.Equal("passed", completed.GetProperty("verification_status").GetString());
        Assert.Equal(64, started.GetProperty("audit_stream_ref").GetString()!.Length);
        Assert.Matches("^[0-9a-f]{64}$", writer.AuditStreamRef);
        Assert.Equal(writer.AuditStreamRef, started.GetProperty("audit_stream_ref").GetString());
        Assert.Matches("^[0-9a-f]{64}$", started.GetProperty("intent_ref_hash").GetString());
        Assert.DoesNotContain("\r", File.ReadAllText(OnlyFile(sandbox.Directory)), StringComparison.Ordinal);
    }

    [Fact]
    public void Concurrent_appends_do_not_interleave_or_duplicate_sequence()
    {
        using var sandbox = new Sandbox();
        using var writer = sandbox.Open();
        var statuses = new AuditAppendStatus[24];
        Parallel.For(0, statuses.Length, index =>
        {
            statuses[index] = writer.TryAppendApplyStarted(Started());
        });
        writer.Stop();

        Assert.All(statuses, status => Assert.Equal(AuditAppendStatus.Durable, status));
        var lines = Lines(sandbox.Directory);
        Assert.Equal(statuses.Length, lines.Count);
        var sequences = lines.Select(line => JsonDocument.Parse(line).RootElement.GetProperty("sequence").GetInt32()).OrderBy(value => value).ToArray();
        Assert.Equal(Enumerable.Range(1, statuses.Length).ToArray(), sequences);
    }

    [Fact]
    public void Hashes_are_domain_separated_and_raw_identifiers_stay_out_of_the_file()
    {
        const string intent = "raw-intent-secret";
        const string instance = "raw-instance-secret";
        const string document = "raw-document-secret";
        using var sandbox = new Sandbox();
        using var writer = sandbox.Open();
        Assert.Equal(AuditAppendStatus.Durable, writer.TryAppendApplyStarted(Started()));
        writer.Stop();

        var text = File.ReadAllText(OnlyFile(sandbox.Directory));
        Assert.DoesNotContain(intent, text, StringComparison.Ordinal);
        Assert.DoesNotContain(instance, text, StringComparison.Ordinal);
        Assert.DoesNotContain(document, text, StringComparison.Ordinal);
        Assert.DoesNotContain("element_ref", text, StringComparison.Ordinal);
        Assert.DoesNotContain("parameter_ref", text, StringComparison.Ordinal);
        Assert.DoesNotContain("revit-local-in-process", text, StringComparison.Ordinal);
        var root = JsonDocument.Parse(Lines(sandbox.Directory)[0]).RootElement;
        Assert.Equal(Hash("revitmcp-audit-v1:intent_ref:" + intent), root.GetProperty("intent_ref_hash").GetString());
        Assert.Equal(Hash("revitmcp-audit-v1:instance_id:" + instance), root.GetProperty("instance_id_hash").GetString());
        Assert.Equal(Hash("revitmcp-audit-v1:document_id:" + document), root.GetProperty("document_id_hash").GetString());
        Assert.NotEqual(ControlledWriteAuditWriter.HashIntentRef(intent), ControlledWriteAuditWriter.HashInstanceId(intent));
        Assert.NotEqual(ControlledWriteAuditWriter.HashInstanceId(instance), ControlledWriteAuditWriter.HashDocumentId(instance));
    }

    [Fact]
    public void Approval_method_maps_only_the_exact_provider_value()
    {
        Assert.True(ControlledWriteAuditWriter.TryMapApprovalMethod("revit-local-in-process", out var mapped));
        Assert.Equal("revit_local_in_process", mapped);
        Assert.False(ControlledWriteAuditWriter.TryMapApprovalMethod("revit_local_in_process", out _));
        Assert.False(ControlledWriteAuditWriter.TryMapApprovalMethod("Revit-Local-In-Process", out _));
        Assert.False(ControlledWriteAuditWriter.TryMapApprovalMethod("other-provider", out _));

        using var sandbox = new Sandbox();
        using var writer = sandbox.Open();
        var rejected = Started();
        rejected = WithMethod(rejected, "other-provider");
        Assert.Equal(AuditAppendStatus.Rejected, writer.TryAppendApplyStarted(rejected));
        Assert.False(writer.IsDegraded);
        writer.Stop();
        Assert.Empty(Directory.GetFiles(sandbox.Directory));
    }

    [Fact]
    public void Oversized_line_is_rejected_without_truncation_or_degradation()
    {
        using var sandbox = new Sandbox();
        sandbox.Options.MaxLineBytes = 128;
        using var writer = sandbox.Open();

        Assert.Equal(AuditAppendStatus.Rejected, writer.TryAppendApplyStarted(Started()));

        Assert.False(writer.IsDegraded);
        writer.Stop();
        var files = Directory.GetFiles(sandbox.Directory);
        Assert.All(files, path => Assert.Equal(0, new FileInfo(path).Length));
    }

    [Fact]
    public void Preflight_requires_headroom_and_keeps_the_admission_threshold()
    {
        Assert.Equal(67_108_864, new AuditWriterOptions().AdmissionBytes);
        Assert.Equal(16_384, new AuditWriterOptions().HeadroomBytes);
        Assert.Equal(8192, new AuditWriterOptions().MaxLineBytes);
        Assert.Equal(TimeSpan.FromDays(30), new AuditWriterOptions().Retention);

        using var sandbox = new Sandbox();
        sandbox.Options.AdmissionBytes = 20_000;
        sandbox.Options.HeadroomBytes = 16_384;
        File.WriteAllBytes(Path.Combine(sandbox.Directory, "notes.txt"), new byte[20_000 - 16_384]);
        using var writer = sandbox.Open();
        Assert.True(writer.Preflight("2026", "2026.5", "0.1").Succeeded);

        File.WriteAllBytes(Path.Combine(sandbox.Directory, "notes-2.txt"), new byte[] { 1 });
        Assert.False(writer.Preflight("2026", "2026.5", "0.1").Succeeded);
        Assert.True(File.Exists(Path.Combine(sandbox.Directory, "notes.txt")));
        Assert.True(File.Exists(Path.Combine(sandbox.Directory, "notes-2.txt")));
    }

    [Fact]
    public void Housekeeping_deletes_expired_files_and_protects_the_active_stream()
    {
        using var sandbox = new Sandbox();
        sandbox.Clock.UtcNow = new DateTimeOffset(2026, 10, 7, 0, 0, 0, TimeSpan.Zero);
        var expired = Path.Combine(sandbox.Directory, "write-20200101-" + Hex('e') + ".jsonl");
        var retained = Path.Combine(sandbox.Directory, "write-20261220-" + Hex('f') + ".jsonl");
        File.WriteAllText(expired, "{}\n");
        File.WriteAllText(retained, "{}\n");
        using var writer = sandbox.Open();
        Assert.Equal(AuditAppendStatus.Durable, writer.TryAppendApplyStarted(Started()));
        var active = OnlyFile(sandbox.Directory, skip: new[] { expired, retained });

        sandbox.Clock.UtcNow = new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.Zero);
        Assert.True(writer.Preflight("2026", "2026.5", "0.1").Succeeded);

        Assert.False(File.Exists(expired));
        Assert.True(File.Exists(retained));
        Assert.True(File.Exists(active));
    }

    [Fact]
    public void In_use_expired_file_remains_when_deletion_fails()
    {
        using var sandbox = new Sandbox();
        sandbox.Clock.UtcNow = new DateTimeOffset(2026, 10, 7, 0, 0, 0, TimeSpan.Zero);
        var expired = Path.Combine(sandbox.Directory, "write-20200101-" + Hex('a') + ".jsonl");
        File.WriteAllText(expired, "{}\n");
        using var held = new FileStream(expired, FileMode.Open, FileAccess.Read, FileShare.None);
        using var writer = sandbox.Open();

        Assert.True(writer.Preflight("2026", "2026.5", "0.1").Succeeded);

        Assert.True(File.Exists(expired));
        held.Dispose();
    }

    [Fact]
    public void Utc_date_rotation_starts_a_new_stream_file_and_sequence()
    {
        using var sandbox = new Sandbox();
        sandbox.Clock.UtcNow = new DateTimeOffset(2026, 10, 7, 23, 0, 0, TimeSpan.Zero);
        using var writer = sandbox.Open();
        Assert.Equal(AuditAppendStatus.Durable, writer.TryAppendApplyStarted(Started()));
        sandbox.Clock.UtcNow = new DateTimeOffset(2026, 10, 8, 0, 30, 0, TimeSpan.Zero);
        Assert.Equal(AuditAppendStatus.Durable, writer.TryAppendApplyCompleted(Completed()));
        writer.Stop();

        var files = Directory.GetFiles(sandbox.Directory).Select(Path.GetFileName).OrderBy(name => name).ToArray();
        Assert.Equal(2, files.Length);
        Assert.Contains("write-20261007-" + writer.AuditStreamRef + ".jsonl", files);
        Assert.Contains("write-20261008-" + writer.AuditStreamRef + ".jsonl", files);
        Assert.All(files, name => Assert.Matches("^write-[0-9]{8}-[0-9a-f]{64}\\.jsonl$", name!));
        var first = JsonDocument.Parse(File.ReadAllLines(Path.Combine(sandbox.Directory, files[0]!))[0]);
        var second = JsonDocument.Parse(File.ReadAllLines(Path.Combine(sandbox.Directory, files[1]!))[0]);
        Assert.Equal(1, first.RootElement.GetProperty("sequence").GetInt32());
        Assert.Equal(1, second.RootElement.GetProperty("sequence").GetInt32());
    }

    [Fact]
    public void Active_file_allows_read_sharing_and_refuses_write_sharing()
    {
        using var sandbox = new Sandbox();
        using var writer = sandbox.Open();
        Assert.Equal(AuditAppendStatus.Durable, writer.TryAppendApplyStarted(Started()));
        var path = OnlyFile(sandbox.Directory);

        using (var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            Assert.True(reader.CanRead);
        }

        Assert.Throws<IOException>(() => new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite));
    }

    [Fact]
    public void Flush_failure_degrades_the_writer_until_preflight_recovers()
    {
        using var sandbox = new Sandbox();
        sandbox.Options.FailFlush = true;
        using var writer = sandbox.Open();
        Assert.Equal(AuditAppendStatus.Failed, writer.TryAppendApplyStarted(Started()));
        Assert.True(writer.IsDegraded);
        Assert.Equal(AuditAppendStatus.Failed, writer.TryAppendApplyCompleted(Completed()));

        sandbox.Options.FailFlush = false;
        Assert.True(writer.Preflight("2026", "2026.5", "0.1").Succeeded);
        Assert.False(writer.IsDegraded);
        Assert.Equal(AuditAppendStatus.Durable, writer.TryAppendApplyCompleted(Completed()));
    }

    [Fact]
    public void Pending_completion_and_unbounded_metadata_are_rejected()
    {
        using var sandbox = new Sandbox();
        using var writer = sandbox.Open();
        var pending = Completed();
        pending = WithTransaction(pending, AuditTransactionStatus.Pending);
        Assert.Equal(AuditAppendStatus.Rejected, writer.TryAppendApplyCompleted(pending));
        var uppercase = Started();
        uppercase = WithAttempt(uppercase, new string('A', 64));
        Assert.Equal(AuditAppendStatus.Rejected, writer.TryAppendApplyStarted(uppercase));
        Assert.False(writer.Preflight(new string('v', 33), "2026.5", "0.1").Succeeded);
        Assert.False(writer.IsDegraded);
        writer.Stop();
        Assert.Empty(Directory.GetFiles(sandbox.Directory));
    }

    [Fact]
    public void Storage_failure_does_not_throw_or_block_intent_creation_or_lifetime_stop()
    {
        using var sandbox = new Sandbox();
        var blocked = Path.Combine(sandbox.Directory, "blocked");
        File.WriteAllText(blocked, "x");
        using var writer = new ControlledWriteAuditWriter(options: new AuditWriterOptions { Directory = blocked });
        var preflight = writer.Preflight("2026", "2026.5", "0.1");
        Assert.False(preflight.Succeeded);
        Assert.True(writer.IsDegraded);

        var intents = new EphemeralWriteIntentStore(randomBytes: () => new byte[32]);
        var created = intents.TryCreate(new IntentDraft
        {
            InstanceId = "instance-a",
            DocumentId = "doc-a",
            Items = new List<IntentItemDraft>
            {
                new()
                {
                    RequestPosition = 1,
                    ElementRef = "element-a",
                    ParameterRef = "parameter-a",
                    Source = "instance",
                    IdentityKind = DescribeParameterIdentityKind.Local,
                    StableKey = "local:42",
                    Status = "ok",
                    ElementName = "Wall 1",
                    CategoryName = "Walls",
                    ParameterName = "Comments",
                    DataTypeKind = DescribeParameterDataTypeKind.Spec,
                    Proposed = new IntentTypedValue.StringValue("proposed")
                }
            }
        });
        Assert.Equal(IntentStoreCreateStatus.Created, created.Status);

        var auditRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RevitMCP",
            "Audit",
            "v1");
        var before = Directory.Exists(auditRoot) ? Directory.GetFiles(auditRoot).Length : -1;
        var lifetime = new RevitExecutionDispatcherLifetime(intents, () => { }, () => { });
        lifetime.Stop();
        lifetime.Dispose();
        var after = Directory.Exists(auditRoot) ? Directory.GetFiles(auditRoot).Length : -1;
        Assert.Equal(before, after);
    }

    [Fact]
    public void Stop_and_dispose_are_idempotent()
    {
        using var sandbox = new Sandbox();
        var writer = sandbox.Open();
        Assert.Equal(AuditAppendStatus.Durable, writer.TryAppendApplyStarted(Started()));
        writer.Stop();
        writer.Stop();
        writer.Dispose();
        writer.Dispose();
        Assert.Equal(AuditAppendStatus.Rejected, writer.TryAppendApplyStarted(Started()));
    }

    [Fact]
    public void Production_root_uses_local_application_data_without_a_hard_coded_user_path()
    {
        var source = File.ReadAllText(RepoFile(Path.Combine("src", "RevitMCP.Addin", "Audit", "ControlledWriteAuditWriter.cs")));
        Assert.Contains("Environment.SpecialFolder.LocalApplicationData", source, StringComparison.Ordinal);
        Assert.Contains("RevitMCP", source, StringComparison.Ordinal);
        Assert.Contains("Audit", source, StringComparison.Ordinal);
        Assert.Contains("v1", source, StringComparison.Ordinal);
        Assert.DoesNotContain(@"C:\Users", source, StringComparison.Ordinal);
        Assert.Contains("Flush(flushToDisk: true)", source, StringComparison.Ordinal);
        Assert.Contains("FileShare.Read", source, StringComparison.Ordinal);
        Assert.Contains("FileMode.CreateNew", source, StringComparison.Ordinal);
    }

    private static AuditEventDraft Started(char attempt = 'b')
    {
        var when = new DateTimeOffset(2026, 10, 7, 15, 4, 5, TimeSpan.Zero).AddTicks(1234567);
        return new AuditEventDraft
        {
            EventAtUtc = when,
            AttemptRef = Hex(attempt),
            IntentRef = "raw-intent-secret",
            IntentFingerprint = Hex('c'),
            IntentFingerprintSchemaVersion = 1,
            InstanceId = "raw-instance-secret",
            DocumentId = "raw-document-secret",
            RevitVersion = "2026",
            RevitBuild = "2026.5",
            AddinVersion = "0.1.0",
            ItemCount = 2,
            ApprovalMethod = ControlledWriteAuditWriter.ApprovalMethodSource,
            ApprovalDecidedAtUtc = when,
            ApprovalEffectiveExpiryUtc = when.AddMinutes(5)
        };
    }

    private static AuditEventDraft Completed()
    {
        var started = Started('d');
        return new AuditEventDraft
        {
            EventAtUtc = started.EventAtUtc,
            AttemptRef = started.AttemptRef,
            IntentRef = started.IntentRef,
            IntentFingerprint = started.IntentFingerprint,
            IntentFingerprintSchemaVersion = started.IntentFingerprintSchemaVersion,
            InstanceId = started.InstanceId,
            DocumentId = started.DocumentId,
            RevitVersion = started.RevitVersion,
            RevitBuild = started.RevitBuild,
            AddinVersion = started.AddinVersion,
            ItemCount = started.ItemCount,
            ApplyStatus = AuditApplyStatus.Applied,
            TransactionStatus = AuditTransactionStatus.Committed,
            VerificationStatus = AuditVerificationStatus.Passed
        };
    }

    private static AuditEventDraft WithMethod(AuditEventDraft draft, string method)
    {
        return new AuditEventDraft
        {
            EventAtUtc = draft.EventAtUtc,
            AttemptRef = draft.AttemptRef,
            IntentRef = draft.IntentRef,
            IntentFingerprint = draft.IntentFingerprint,
            IntentFingerprintSchemaVersion = draft.IntentFingerprintSchemaVersion,
            InstanceId = draft.InstanceId,
            DocumentId = draft.DocumentId,
            RevitVersion = draft.RevitVersion,
            RevitBuild = draft.RevitBuild,
            AddinVersion = draft.AddinVersion,
            ItemCount = draft.ItemCount,
            ApprovalMethod = method,
            ApprovalDecidedAtUtc = draft.ApprovalDecidedAtUtc,
            ApprovalEffectiveExpiryUtc = draft.ApprovalEffectiveExpiryUtc
        };
    }

    private static AuditEventDraft WithAttempt(AuditEventDraft draft, string attempt)
    {
        return new AuditEventDraft
        {
            EventAtUtc = draft.EventAtUtc,
            AttemptRef = attempt,
            IntentRef = draft.IntentRef,
            IntentFingerprint = draft.IntentFingerprint,
            IntentFingerprintSchemaVersion = draft.IntentFingerprintSchemaVersion,
            InstanceId = draft.InstanceId,
            DocumentId = draft.DocumentId,
            RevitVersion = draft.RevitVersion,
            RevitBuild = draft.RevitBuild,
            AddinVersion = draft.AddinVersion,
            ItemCount = draft.ItemCount,
            ApprovalMethod = draft.ApprovalMethod,
            ApprovalDecidedAtUtc = draft.ApprovalDecidedAtUtc,
            ApprovalEffectiveExpiryUtc = draft.ApprovalEffectiveExpiryUtc
        };
    }

    private static AuditEventDraft WithTransaction(AuditEventDraft draft, AuditTransactionStatus status)
    {
        return new AuditEventDraft
        {
            EventAtUtc = draft.EventAtUtc,
            AttemptRef = draft.AttemptRef,
            IntentRef = draft.IntentRef,
            IntentFingerprint = draft.IntentFingerprint,
            IntentFingerprintSchemaVersion = draft.IntentFingerprintSchemaVersion,
            InstanceId = draft.InstanceId,
            DocumentId = draft.DocumentId,
            RevitVersion = draft.RevitVersion,
            RevitBuild = draft.RevitBuild,
            AddinVersion = draft.AddinVersion,
            ItemCount = draft.ItemCount,
            ApplyStatus = draft.ApplyStatus,
            TransactionStatus = status,
            VerificationStatus = draft.VerificationStatus
        };
    }

    private static string Hash(string text)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }

    private static string Hex(char digit) => new(digit, 64);

    private static List<string> Lines(string directory)
    {
        return Directory.GetFiles(directory)
            .OrderBy(path => path, StringComparer.Ordinal)
            .SelectMany(path => File.ReadAllLines(path))
            .Where(line => line.Length > 0)
            .ToList();
    }

    private static string OnlyFile(string directory, string[]? skip = null)
    {
        return Directory.GetFiles(directory).Single(path => skip is null || !skip.Contains(path, StringComparer.OrdinalIgnoreCase));
    }

    private static string RepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "RevitMCP.sln")))
            {
                return Path.Combine(dir.FullName, relative);
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Repository root was not found.");
    }

    private sealed class Sandbox : IDisposable
    {
        public Sandbox()
        {
            Directory = Path.Combine(Path.GetTempPath(), "revitmcp-audit-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(Directory);
            Clock = new ManualTimeProvider();
            Options = new AuditWriterOptions { Directory = Directory };
        }

        public string Directory { get; }

        public ManualTimeProvider Clock { get; }

        public AuditWriterOptions Options { get; }

        public ControlledWriteAuditWriter Open() => new(Clock, Options);

        public void Dispose()
        {
            try
            {
                System.IO.Directory.Delete(Directory, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
