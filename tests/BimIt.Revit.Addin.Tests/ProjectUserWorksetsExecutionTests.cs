using System.Text;
using System.Text.Json;
using BimIt.Revit.Addin.Query;
using BimIt.Revit.Addin.ReadOperations;
using BimIt.Revit.Bridge;
using BimIt.Revit.Contracts;
using Xunit;

namespace BimIt.Revit.Addin.Tests;

public sealed class ProjectUserWorksetsExecutionTests
{
    private const string InstanceId = "host-1";

    private const string DocumentId = "doc-1";

    private readonly byte[] _key = Encoding.ASCII.GetBytes("0123456789abcdef0123456789abcdef");

    [Fact]
    public async Task Schema_and_semantic_failures_do_not_enter_execution()
    {
        var queue = new FlagQueue();
        var service = Service(queue);
        var pipeline = Pipeline();

        var omitted = Assert.IsType<UserWorksetsRequest>(pipeline.Prepare(Request(ProjectUserWorksetsReadOperation.OperationId, 1, "{}")).Request);
        Assert.Null(omitted.NameContains);
        var named = Assert.IsType<UserWorksetsRequest>(pipeline.Prepare(Request(ProjectUserWorksetsReadOperation.OperationId, 1, """{"name_contains":"level"}""")).Request);
        Assert.Equal("level", named.NameContains);

        var unknown = await RejectsBeforeEnqueue(queue, service, """{"other":"secret-value"}""", CapabilityErrorCodes.InvalidReadOperationInput);
        Assert.Contains("Accepted inputs: name_contains.", unknown.Message, StringComparison.Ordinal);
        Assert.Contains("Constraint additionalProperties failed at $.other.", unknown.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-value", unknown.Message, StringComparison.Ordinal);

        var empty = await RejectsBeforeEnqueue(queue, service, """{"name_contains":""}""", CapabilityErrorCodes.InvalidReadOperationInput);
        Assert.Contains("Constraint minLength failed at $.name_contains.", empty.Message, StringComparison.Ordinal);

        var tooLong = new string('a', 257);
        var length = await RejectsBeforeEnqueue(queue, service, "{\"name_contains\":\"" + tooLong + "\"}", CapabilityErrorCodes.InvalidReadOperationInput);
        Assert.Contains("Constraint maxLength failed at $.name_contains.", length.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(tooLong, length.Message, StringComparison.Ordinal);

        var wrongType = await RejectsBeforeEnqueue(queue, service, """{"name_contains":1}""", CapabilityErrorCodes.InvalidReadOperationInput);
        Assert.Contains("Constraint type failed at $.name_contains.", wrongType.Message, StringComparison.Ordinal);

        var whitespace = await RejectsBeforeEnqueue(queue, service, """{"name_contains":"   "}""", CapabilityErrorCodes.InvalidReadOperationInput);
        Assert.Contains("name_contains is invalid.", whitespace.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Constraint", whitespace.Message, StringComparison.Ordinal);

        var bindingQueue = new FlagQueue();
        var binding = Service(bindingQueue, new ThrowingBindOperation(new ProjectUserWorksetsReadOperation().Descriptor));
        await RejectsBeforeEnqueue(bindingQueue, binding, "{}", CapabilityErrorCodes.InvalidReadOperationInput);

        var explodingQueue = new FlagQueue();
        var exploding = Service(explodingQueue, new ExplodingOperation(new ProjectUserWorksetsReadOperation().Descriptor));
        var unexpected = await RejectsBeforeEnqueue(explodingQueue, exploding, "{}", CapabilityErrorCodes.ExecutionFailed);
        Assert.Equal("The read operation could not be read.", unexpected.Message);
        Assert.DoesNotContain("boom", unexpected.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Unknown_id_and_unsupported_version_stay_distinct_and_use_the_snapshot()
    {
        var operation = new CountingOperation(new ProjectUserWorksetsReadOperation());
        var queue = new FlagQueue();
        var service = Service(queue, operation);
        Assert.Equal(1, operation.Reads);

        var unknown = await Assert.ThrowsAsync<BridgeException>(() => service.ReadAsync(Request("Project.user_worksets", 1, "{}"), CancellationToken.None));
        var unsupported = await Assert.ThrowsAsync<BridgeException>(() => service.ReadAsync(Request(ProjectUserWorksetsReadOperation.OperationId, 2, "{}"), CancellationToken.None));

        Assert.Equal(CapabilityErrorCodes.UnknownReadOperation, unknown.ErrorCode);
        Assert.Equal(CapabilityErrorCodes.ReadOperationVersionUnsupported, unsupported.ErrorCode);
        Assert.Contains("1", unsupported.Message, StringComparison.Ordinal);
        Assert.Equal(0, queue.Calls);
        Assert.Equal(1, operation.Reads);
    }

    [Fact]
    public void Filtering_ordering_and_summary_use_the_whole_matched_set()
    {
        var rows = Inventory(
            ("b", false, true),
            ("B", true, false),
            ("a", false, false),
            ("Same", false, true),
            ("Same", true, false));
        var page = Execute(rows, null, pageSize: 100);
        Assert.Equal(["a", "B", "b", "Same", "Same"], Names(page));
        Assert.Equal(5, page.Result.GetProperty("summary").GetProperty("matched_workset_count").GetInt32());
        Assert.Equal(5, page.Result.GetProperty("returned_count").GetInt32());
        Assert.True(page.Result.GetProperty("summary").GetProperty("worksharing_enabled").GetBoolean());
        Assert.True(JsonSchemaSubset.MatchesInstance(page.Result, new ProjectUserWorksetsReadOperation().Descriptor.OutputSchema!.Value));

        var filtered = Execute(rows, "b", pageSize: 100);
        Assert.Equal(["B", "b"], Names(filtered));
        Assert.Equal(2, filtered.Result.GetProperty("summary").GetProperty("matched_workset_count").GetInt32());
    }

    [Fact]
    public void Non_workshared_and_unmatched_results_are_empty_successes()
    {
        var page = Execute(new UserWorksetInventory(false, [new UserWorksetCandidate("Hidden", false, false)]), null, 100);
        Assert.False(page.Result.GetProperty("summary").GetProperty("worksharing_enabled").GetBoolean());
        Assert.Equal(0, page.Result.GetProperty("summary").GetProperty("matched_workset_count").GetInt32());
        Assert.Equal(0, page.Result.GetProperty("returned_count").GetInt32());
        Assert.Equal(0, page.Result.GetProperty("worksets").GetArrayLength());
        Assert.True(page.Complete);
        Assert.Null(page.NextCursor);

        var none = Execute(Inventory(), "missing", 100);
        Assert.True(none.Result.GetProperty("summary").GetProperty("worksharing_enabled").GetBoolean());
        Assert.Equal(0, none.Result.GetProperty("worksets").GetArrayLength());
        Assert.True(none.Complete);
        Assert.Null(none.NextCursor);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    [InlineData(200)]
    public void Pages_concatenate_to_the_ordered_set_once(int pageSize)
    {
        var names = new[] { "d", "b", "c", "a", "e" };
        var collected = Walk(Inventory(names.Select(name => (name, false, true)).ToArray()), pageSize);
        Assert.Equal(["a", "b", "c", "d", "e"], collected);
    }

    [Fact]
    public void Page_size_can_change_between_pages_and_is_not_part_of_the_fingerprint()
    {
        var inventory = Inventory(("a", false, true), ("b", false, true), ("c", false, true), ("d", false, true));
        var pipeline = Pipeline();
        var first = Page(pipeline, inventory, null, 1, null);
        var second = Page(pipeline, inventory, null, 2, first.NextCursor);
        var third = Page(pipeline, inventory, null, 100, second.NextCursor);
        Assert.Equal(["a", "b", "c", "d"], Names(first).Concat(Names(second)).Concat(Names(third)).ToArray());
        Assert.True(third.Complete);

        var request = new UserWorksetsRequest { NameContains = "Level" };
        var descriptor = new ProjectUserWorksetsReadOperation().Descriptor;
        Assert.Equal(
            ProjectUserWorksetsReadOperation.FingerprintFor(request, DocumentId, descriptor),
            ProjectUserWorksetsReadOperation.FingerprintFor(request, DocumentId, descriptor));
        Assert.NotEqual(
            ProjectUserWorksetsReadOperation.FingerprintFor(request, DocumentId, descriptor),
            ProjectUserWorksetsReadOperation.FingerprintFor(new UserWorksetsRequest(), DocumentId, descriptor));
    }

    [Fact]
    public void Cursors_are_isolated_and_a_changed_revision_is_stale_before_the_read()
    {
        var probe = new ProbeOperation(new ProjectUserWorksetsReadOperation().Descriptor);
        var pipeline = Pipeline(probe);
        var codec = pipeline.Cursors;
        var minted = Mint(codec, """{"name_contains":"Alpha"}""", DocumentId, InstanceId, revision: 0, offset: 0);
        Assert.Equal(CapabilityErrorCodes.InvalidCursor, PrepareError(pipeline, Request(ProjectUserWorksetsReadOperation.OperationId, 1, """{"name_contains":"Alpha"}""", cursor: minted + "x")));
        Assert.Equal(CapabilityErrorCodes.InvalidCursor, PrepareError(pipeline, Request(ProjectUserWorksetsReadOperation.OperationId, 1, """{"name_contains":"Alpha"}""", cursor: Mint(codec, """{"name_contains":"Alpha"}""", DocumentId, "other-host", 0, 0))));
        Assert.Equal(CapabilityErrorCodes.InvalidCursor, PrepareError(pipeline, Request(ProjectUserWorksetsReadOperation.OperationId, 1, """{"name_contains":"Alpha"}""", cursor: Mint(codec, """{"name_contains":"Alpha"}""", "other-doc", InstanceId, 0, 0))));
        Assert.Equal(CapabilityErrorCodes.InvalidCursor, PrepareError(pipeline, Request(ProjectUserWorksetsReadOperation.OperationId, 1, """{"name_contains":"Beta"}""", cursor: minted)));
        Assert.Equal(CapabilityErrorCodes.InvalidCursor, PrepareError(pipeline, Request(ProjectUserWorksetsReadOperation.OperationId, 1, "{}", cursor: minted)));
        Assert.Equal(CapabilityErrorCodes.InvalidCursor, PrepareError(pipeline, Request(ProjectUserWorksetsReadOperation.OperationId, 1, """{"name_contains":"Alpha"}""", cursor: ForeignCursor(codec, DeclarativeQuerySources.FamilyTypes))));
        Assert.Equal(CapabilityErrorCodes.InvalidCursor, PrepareError(pipeline, Request(ProjectUserWorksetsReadOperation.OperationId, 1, """{"name_contains":"Alpha"}""", cursor: ForeignCursor(codec, DeclarativeQuerySources.ParameterBindings))));

        var staleDocument = new FakeDocument();
        var staleCursor = Mint(codec, """{"name_contains":"Alpha"}""", DocumentId, InstanceId, revision: 0, offset: 0);
        pipeline.Revisions.Advance(DocumentId);
        var prepared = pipeline.Prepare(Request(ProjectUserWorksetsReadOperation.OperationId, 1, """{"name_contains":"Alpha"}""", cursor: staleCursor));
        var stale = Assert.Throws<BridgeException>(() => pipeline.Execute(prepared, staleDocument));
        Assert.Equal(CapabilityErrorCodes.StaleCursor, stale.ErrorCode);
        Assert.Equal(0, probe.Executions);
    }

    [Fact]
    public void Document_guards_run_before_the_workset_read()
    {
        var probe = new ProbeOperation(new ProjectUserWorksetsReadOperation().Descriptor);
        var pipeline = Pipeline(probe);
        var prepared = pipeline.Prepare(Request(ProjectUserWorksetsReadOperation.OperationId, 1, "{}"));
        var family = new FakeDocument { IsFamilyDocument = true };
        var missing = new FakeDocument { HasActiveDocument = false };
        var changed = new FakeDocument { DocumentId = "other" };

        Assert.Equal(CapabilityErrorCodes.UnsupportedDocumentKind, Assert.Throws<BridgeException>(() => pipeline.Execute(prepared, family)).ErrorCode);
        Assert.Equal(CapabilityErrorCodes.NoActiveDocument, Assert.Throws<BridgeException>(() => pipeline.Execute(prepared, missing)).ErrorCode);
        Assert.Equal(CapabilityErrorCodes.DocumentContextChanged, Assert.Throws<BridgeException>(() => pipeline.Execute(prepared, changed)).ErrorCode);
        Assert.Equal(0, probe.Executions);
    }

    [Fact]
    public void An_undeclared_output_property_returns_no_payload()
    {
        var operation = new DriftOperation();
        var pipeline = Pipeline(operation);
        var prepared = pipeline.Prepare(Request(operation.Descriptor.Id, 1, "{}"));
        var error = Assert.Throws<BridgeException>(() => pipeline.Execute(prepared, new FakeDocument()));
        Assert.Equal(CapabilityErrorCodes.ReadOperationResultInvalid, error.ErrorCode);
    }

    [Fact]
    public void Oversized_pages_shorten_in_order_and_one_impossible_row_is_too_large()
    {
        var names = Enumerable.Range(0, 8).Select(index => index.ToString("D2") + new string('x', 20_000)).ToArray();
        var inventory = Inventory(names.Select(name => (name, false, true)).ToArray());
        var collected = Walk(inventory, pageSize: 100);
        Assert.Equal(names, collected);
        var first = Page(Pipeline(), inventory, null, 100, null);
        Assert.True(Names(first).Count < names.Length);
        Assert.False(first.Complete);

        var huge = Inventory((new string('y', 130_000), false, true));
        var tooLarge = Assert.Throws<BridgeException>(() => Execute(huge, null, 1));
        Assert.Equal(CapabilityErrorCodes.ResultTooLarge, tooLarge.ErrorCode);

        var dispatcher = new UnfittedOperation();
        var unfitted = Pipeline(dispatcher);
        var prepared = unfitted.Prepare(Request(dispatcher.Descriptor.Id, 1, "{}"));
        var measured = Assert.Throws<BridgeException>(() => unfitted.Execute(prepared, new FakeDocument()));
        Assert.Equal(CapabilityErrorCodes.ResultTooLarge, measured.ErrorCode);
    }

    [Fact]
    public async Task Cancellation_passes_through_before_execution()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var queue = new FlagQueue();
        var service = Service(queue);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ReadAsync(
            Request(ProjectUserWorksetsReadOperation.OperationId, 1, "{}"),
            cancelled.Token));
        Assert.Equal(0, queue.Calls);
    }

    private static async Task<BridgeException> RejectsBeforeEnqueue(FlagQueue queue, RevitReadOperationService service, string inputs, string code)
    {
        var before = queue.Calls;
        var error = await Assert.ThrowsAsync<BridgeException>(() => service.ReadAsync(Request(ProjectUserWorksetsReadOperation.OperationId, 1, inputs), CancellationToken.None));
        Assert.Equal(code, error.ErrorCode);
        Assert.Equal(before, queue.Calls);
        return error;
    }

    private ReadOperationResult Execute(UserWorksetInventory inventory, string? nameContains, int pageSize)
    {
        return Page(Pipeline(), inventory, nameContains, pageSize, null);
    }

    private ReadOperationResult Page(TestPipeline pipeline, UserWorksetInventory inventory, string? nameContains, int pageSize, string? cursor)
    {
        var inputs = nameContains is null ? "{}" : "{\"name_contains\":" + JsonSerializer.Serialize(nameContains) + "}";
        var prepared = pipeline.Prepare(Request(ProjectUserWorksetsReadOperation.OperationId, 1, inputs, pageSize, cursor));
        var page = ProjectUserWorksetsQuery.Complete(
            (UserWorksetsRequest)prepared.Request,
            Context(prepared, pipeline),
            inventory);
        var payload = ReadOperationResults.Payload(page.TypedResult);
        Assert.True(JsonSchemaSubset.MatchesInstance(payload, prepared.Descriptor.OutputSchema!.Value));
        var envelope = ReadOperationResults.Envelope(
            InstanceId,
            DocumentId,
            prepared.Descriptor.Id,
            prepared.Descriptor.ContractVersion,
            payload,
            page.Complete,
            page.NextCursor);
        if (ReadResultBudget.Measure(envelope) > ReadResultBudget.MaxCharacters)
        {
            throw new BridgeException(CapabilityErrorCodes.ResultTooLarge, "The read operation result is too large.");
        }

        return envelope;
    }

    private static ReadOperationExecutionContext Context(PreparedReadOperation prepared, TestPipeline pipeline)
    {
        return new ReadOperationExecutionContext
        {
            Descriptor = prepared.Descriptor,
            InstanceId = InstanceId,
            DocumentId = DocumentId,
            PageSize = prepared.PageSize,
            Offset = prepared.Cursor?.Offset ?? 0,
            Revision = pipeline.Revisions.Current(DocumentId),
            Cursors = pipeline.Cursors,
            Host = new FakeDocument()
        };
    }

    private List<string> Walk(UserWorksetInventory inventory, int pageSize)
    {
        var pipeline = Pipeline();
        var collected = new List<string>();
        string? cursor = null;
        for (var guard = 0; guard < 30; guard++)
        {
            var page = Page(pipeline, inventory, null, pageSize, cursor);
            collected.AddRange(Names(page));
            if (page.Complete)
            {
                Assert.Null(page.NextCursor);
                return collected;
            }

            cursor = page.NextCursor;
            Assert.False(string.IsNullOrEmpty(cursor));
        }

        throw new InvalidOperationException("Paging did not complete.");
    }

    private static List<string> Names(ReadOperationResult result)
    {
        return result.Result.GetProperty("worksets").EnumerateArray().Select(row => row.GetProperty("name").GetString()!).ToList();
    }

    private string PrepareError(TestPipeline pipeline, ReadOperationRequest request)
    {
        return Assert.Throws<BridgeException>(() => pipeline.Prepare(request)).ErrorCode;
    }

    private string Mint(DeclarativeQueryCursorCodec codec, string inputs, string documentId, string instanceId, long revision, int offset)
    {
        var operation = new ProjectUserWorksetsReadOperation();
        var request = JsonSerializer.Deserialize<UserWorksetsRequest>(inputs, ReadResultBudget.Options)!;
        var fingerprint = ProjectUserWorksetsReadOperation.FingerprintFor(request, DocumentId, operation.Descriptor);
        Assert.True(codec.TryMint(
            new DeclarativeQueryCursorPayload(operation.Descriptor.Id, instanceId, documentId, fingerprint, revision, offset),
            out var token));
        return token;
    }

    private static string ForeignCursor(DeclarativeQueryCursorCodec codec, string source)
    {
        var fingerprint = new DeclarativeQueryFingerprintBuilder(source).Value("all").Compute();
        Assert.True(codec.TryMint(
            new DeclarativeQueryCursorPayload(source, InstanceId, DocumentId, fingerprint, 0, 0),
            out var token));
        return token;
    }

    private TestPipeline Pipeline(IExecutableReadOperation? operation = null)
    {
        var codec = new DeclarativeQueryCursorCodec(_key);
        var revisions = new DocumentQueryRevisionService();
        var registry = new ReadOperationRegistry([operation ?? new ProjectUserWorksetsReadOperation()]);
        return new TestPipeline(new ReadOperationPipeline(registry, InstanceId, codec, revisions), codec, revisions);
    }

    private RevitReadOperationService Service(FlagQueue queue, IExecutableReadOperation? operation = null)
    {
        queue.Document = new FakeDocument();
        var pipeline = Pipeline(operation);
        return new RevitReadOperationService(pipeline.Pipeline, queue);
    }

    private static ReadOperationRequest Request(string operation, int? version, string inputs, int? pageSize = null, string? cursor = null)
    {
        using var document = JsonDocument.Parse(inputs);
        return new ReadOperationRequest
        {
            DocumentId = DocumentId,
            Operation = operation,
            ContractVersion = version,
            Inputs = document.RootElement.Clone(),
            PageSize = pageSize,
            Cursor = cursor
        };
    }

    private static UserWorksetInventory Inventory(params (string Name, bool IsDefault, bool IsVisible)[] rows)
    {
        return new UserWorksetInventory(true, rows.Select(row => new UserWorksetCandidate(row.Name, row.IsDefault, row.IsVisible)).ToArray());
    }

    private sealed class TestPipeline(ReadOperationPipeline pipeline, DeclarativeQueryCursorCodec cursors, DocumentQueryRevisionService revisions)
    {
        public ReadOperationPipeline Pipeline { get; } = pipeline;

        public DeclarativeQueryCursorCodec Cursors { get; } = cursors;

        public DocumentQueryRevisionService Revisions { get; } = revisions;

        public PreparedReadOperation Prepare(ReadOperationRequest request) => Pipeline.Prepare(request);

        public ReadOperationResult Execute(PreparedReadOperation prepared, IReadOperationDocument document) => Pipeline.Execute(prepared, document);
    }

    private sealed class FlagQueue : IReadOperationWorkQueue
    {
        public int Calls { get; private set; }

        public IReadOperationDocument? Document { get; set; }

        public Task<ReadOperationResult> EnqueueAsync(Func<IReadOperationDocument, ReadOperationResult> execute, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            return Task.FromResult(execute(Document!));
        }
    }

    private sealed class FakeDocument : IReadOperationDocument
    {
        public bool HasActiveDocument { get; init; } = true;

        public bool IsFamilyDocument { get; init; }

        public string DocumentId { get; init; } = ProjectUserWorksetsExecutionTests.DocumentId;

        public string GetDocumentId()
        {
            if (!HasActiveDocument || IsFamilyDocument)
            {
                throw new InvalidOperationException("Document id was read before the document guard.");
            }

            return DocumentId;
        }
    }

    private sealed class ProbeOperation(ReadOperationDescriptor descriptor) : IExecutableReadOperation
    {
        public int Executions { get; private set; }

        public ReadOperationDescriptor Descriptor { get; } = descriptor;

        public object Bind(JsonElement inputs)
            => JsonSerializer.Deserialize<UserWorksetsRequest>(inputs, ReadResultBudget.Options)!;

        public void ValidateInputs(object request)
        {
        }

        public byte[] Fingerprint(object request, string documentId, ReadOperationDescriptor captured)
            => ProjectUserWorksetsReadOperation.FingerprintFor((UserWorksetsRequest)request, documentId, captured);

        public ReadOperationExecutionResult Execute(object request, ReadOperationExecutionContext context)
        {
            Executions++;
            throw new InvalidOperationException("Execution started before the document guard.");
        }

        public void ValidateResult(object result)
        {
        }
    }

    private sealed class ExplodingOperation(ReadOperationDescriptor descriptor) : IExecutableReadOperation
    {
        public ReadOperationDescriptor Descriptor { get; } = descriptor;

        public object Bind(JsonElement inputs) => throw new InvalidOperationException("boom-internal");

        public void ValidateInputs(object request)
        {
        }

        public byte[] Fingerprint(object request, string documentId, ReadOperationDescriptor captured) => [];

        public ReadOperationExecutionResult Execute(object request, ReadOperationExecutionContext context) => throw new InvalidOperationException("boom-internal");

        public void ValidateResult(object result)
        {
        }
    }

    private sealed class CountingOperation(ProjectUserWorksetsReadOperation inner) : IExecutableReadOperation
    {
        private int _reads;

        public int Reads => _reads;

        public ReadOperationDescriptor Descriptor
        {
            get
            {
                if (_reads++ > 0)
                {
                    throw new InvalidOperationException("Descriptor was read again after registration.");
                }

                return inner.Descriptor;
            }
        }

        public object Bind(JsonElement inputs) => inner.Bind(inputs);

        public void ValidateInputs(object request) => inner.ValidateInputs(request);

        public byte[] Fingerprint(object request, string documentId, ReadOperationDescriptor descriptor)
            => inner.Fingerprint(request, documentId, descriptor);

        public ReadOperationExecutionResult Execute(object request, ReadOperationExecutionContext context) => inner.Execute(request, context);

        public void ValidateResult(object result) => inner.ValidateResult(result);
    }

    private sealed class ThrowingBindOperation(ReadOperationDescriptor descriptor) : IExecutableReadOperation
    {
        public ReadOperationDescriptor Descriptor { get; } = descriptor;

        public object Bind(JsonElement inputs) => throw new JsonException("malformed");

        public void ValidateInputs(object request)
        {
        }

        public byte[] Fingerprint(object request, string documentId, ReadOperationDescriptor captured) => [];

        public ReadOperationExecutionResult Execute(object request, ReadOperationExecutionContext context) => throw new InvalidOperationException();

        public void ValidateResult(object result)
        {
        }
    }

    private sealed class DriftOperation : IExecutableReadOperation
    {
        public DriftOperation()
        {
            var source = new ProjectUserWorksetsReadOperation().Descriptor;
            Descriptor = new ReadOperationDescriptor
            {
                Id = "project.sample",
                ContractVersion = 1,
                Title = "Sample",
                Description = "Sample",
                Context = "document",
                Effects = "read",
                Pagination = "cursor",
                InputSchema = source.InputSchema,
                OutputSchema = source.OutputSchema
            };
        }

        public ReadOperationDescriptor Descriptor { get; }

        public object Bind(JsonElement inputs) => new UserWorksetsRequest();

        public void ValidateInputs(object request)
        {
        }

        public byte[] Fingerprint(object request, string documentId, ReadOperationDescriptor descriptor) => [];

        public ReadOperationExecutionResult Execute(object request, ReadOperationExecutionContext context)
        {
            return new ReadOperationExecutionResult
            {
                TypedResult = new DriftResult(),
                Complete = true,
                NextCursor = null
            };
        }

        public void ValidateResult(object result)
        {
        }

        private sealed class DriftResult
        {
            [System.Text.Json.Serialization.JsonPropertyName("summary")]
            public UserWorksetsSummary Summary { get; } = new() { WorksharingEnabled = true, MatchedWorksetCount = 0 };

            [System.Text.Json.Serialization.JsonPropertyName("returned_count")]
            public int ReturnedCount { get; } = 0;

            [System.Text.Json.Serialization.JsonPropertyName("worksets")]
            public UserWorksetRow[] Worksets { get; } = [];

            [System.Text.Json.Serialization.JsonPropertyName("workset_id")]
            public string WorksetId { get; } = "secret";
        }
    }

    private sealed class UnfittedOperation : IExecutableReadOperation
    {
        public UnfittedOperation()
        {
            var source = new ProjectUserWorksetsReadOperation().Descriptor;
            Descriptor = new ReadOperationDescriptor
            {
                Id = "project.sample",
                ContractVersion = 1,
                Title = "Sample",
                Description = "Sample",
                Context = "document",
                Effects = "read",
                Pagination = "none",
                InputSchema = source.InputSchema,
                OutputSchema = source.OutputSchema
            };
        }

        public ReadOperationDescriptor Descriptor { get; }

        public object Bind(JsonElement inputs) => new UserWorksetsRequest();

        public void ValidateInputs(object request)
        {
        }

        public byte[] Fingerprint(object request, string documentId, ReadOperationDescriptor descriptor) => [];

        public ReadOperationExecutionResult Execute(object request, ReadOperationExecutionContext context)
        {
            return new ReadOperationExecutionResult
            {
                TypedResult = new UserWorksetsResult
                {
                    Summary = new UserWorksetsSummary { WorksharingEnabled = true, MatchedWorksetCount = 1 },
                    ReturnedCount = 1,
                    Worksets = [new UserWorksetRow { Name = new string('z', 130_000), IsDefault = false, IsVisibleByDefault = true }]
                },
                Complete = true,
                NextCursor = null
            };
        }

        public void ValidateResult(object result)
        {
        }
    }
}
