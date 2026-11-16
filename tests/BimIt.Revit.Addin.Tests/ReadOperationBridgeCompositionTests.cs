using System.Text.Json;
using BimIt.Revit.Addin.Lifecycle;
using BimIt.Revit.Addin.Query;
using BimIt.Revit.Addin.ReadOperations;
using BimIt.Revit.Bridge;
using BimIt.Revit.Contracts;
using Xunit;

namespace BimIt.Revit.Addin.Tests;

public sealed class ReadOperationBridgeCompositionTests
{
    private const string ClosedObject = """{"type":"object","additionalProperties":false}""";

    [Fact]
    public void Production_list_is_exactly_user_worksets_version_1()
    {
        var operation = Assert.Single(ReadOperationComposition.ProductionOperations());
        Assert.Equal(ProjectUserWorksetsReadOperation.OperationId, operation.Descriptor.Id);
        Assert.Equal(ProjectUserWorksetsReadOperation.Version, operation.Descriptor.ContractVersion);
    }

    [Fact]
    public async Task One_registry_serves_lookup_and_read_and_lookup_does_not_enqueue()
    {
        var queue = new CountingQueue();
        var service = ReadOperationComposition.TryCreate(
            [
                Operation("catalog.item", 2, "Second"),
                Operation("catalog.item", 1, "First")
            ],
            "instance-a",
            new DeclarativeQueryCursorCodec(),
            new DocumentQueryRevisionService(),
            queue);
        Assert.NotNull(service);

        var search = await service.LookupReadOperationsAsync(new LookupReadOperationsRequest(), CancellationToken.None);
        Assert.Equal("search", search.Mode);
        Assert.Equal(0, queue.Calls);
        Assert.Equal(["catalog.item:1", "catalog.item:2"], search.Operations.Select(item => item.Id + ":" + item.ContractVersion).ToArray());
        Assert.All(search.Operations, item =>
        {
            Assert.Null(item.InputSchema);
            Assert.Null(item.OutputSchema);
        });

        var exact = await service.LookupReadOperationsAsync(
            new LookupReadOperationsRequest { Operation = "catalog.item" },
            CancellationToken.None);
        Assert.Equal("exact", exact.Mode);
        Assert.Null(exact.NextOffset);
        Assert.Equal(2, exact.ReturnedCount);
        Assert.All(exact.Operations, item =>
        {
            Assert.NotNull(item.InputSchema);
            Assert.NotNull(item.OutputSchema);
        });
        Assert.Equal(0, queue.Calls);

        var rejected = await Assert.ThrowsAsync<BridgeException>(() => service.ReadOperationAsync(
            new ReadOperationRequest
            {
                Operation = "catalog.item",
                ContractVersion = 2,
                Inputs = Parse("{}")
            },
            CancellationToken.None));
        Assert.Equal(CapabilityErrorCodes.InvalidReadOperationInput, rejected.ErrorCode);
        Assert.Contains("document_id", rejected.Message, StringComparison.Ordinal);
        Assert.Equal(0, queue.Calls);
    }

    [Fact]
    public async Task Lookup_uses_search_defaults_and_rejects_illegal_fields_and_bounds()
    {
        var operations = Enumerable.Range(0, 30)
            .Select(index => Operation("catalog.item" + index.ToString("00"), description: index == 3 ? "keep   spaces" : "Row"))
            .ToArray();
        var service = Service(operations);

        var defaults = await service.LookupReadOperationsAsync(new LookupReadOperationsRequest(), CancellationToken.None);
        Assert.Equal(30, defaults.Total);
        Assert.Equal(25, defaults.ReturnedCount);
        Assert.Equal(25, defaults.NextOffset);
        Assert.Equal("catalog.item00", defaults.Operations[0].Id);

        var page = await service.LookupReadOperationsAsync(
            new LookupReadOperationsRequest { Offset = 20, Limit = 5 },
            CancellationToken.None);
        Assert.Equal(5, page.ReturnedCount);
        Assert.Equal(25, page.NextOffset);
        Assert.Equal(30, page.Total);

        var end = await service.LookupReadOperationsAsync(
            new LookupReadOperationsRequest { Offset = 28, Limit = 10 },
            CancellationToken.None);
        Assert.Equal(2, end.ReturnedCount);
        Assert.Null(end.NextOffset);

        var spaces = await service.LookupReadOperationsAsync(
            new LookupReadOperationsRequest { Query = "   " },
            CancellationToken.None);
        Assert.Equal("catalog.item03", Assert.Single(spaces.Operations).Id);

        var searchVersion = await Assert.ThrowsAsync<BridgeException>(() => service.LookupReadOperationsAsync(
            new LookupReadOperationsRequest { ContractVersion = 1 },
            CancellationToken.None));
        Assert.Equal(CapabilityErrorCodes.InvalidReadOperationLookup, searchVersion.ErrorCode);
        Assert.Contains("contract_version", searchVersion.Message, StringComparison.Ordinal);

        var exactFields = await Assert.ThrowsAsync<BridgeException>(() => service.LookupReadOperationsAsync(
            new LookupReadOperationsRequest { Operation = "catalog.item00", Query = "item", Offset = 1, Limit = 2 },
            CancellationToken.None));
        Assert.Equal(CapabilityErrorCodes.InvalidReadOperationLookup, exactFields.ErrorCode);
        Assert.Contains("query", exactFields.Message, StringComparison.Ordinal);
        Assert.Contains("offset", exactFields.Message, StringComparison.Ordinal);
        Assert.Contains("limit", exactFields.Message, StringComparison.Ordinal);

        await AssertInvalid(new LookupReadOperationsRequest { Query = "" });
        await AssertInvalid(new LookupReadOperationsRequest { Query = new string('a', 257) });
        await AssertInvalid(new LookupReadOperationsRequest { Offset = -1 });
        await AssertInvalid(new LookupReadOperationsRequest { Limit = 0 });
        await AssertInvalid(new LookupReadOperationsRequest { Limit = 51 });
        await AssertInvalid(new LookupReadOperationsRequest { Operation = new string('a', 65) });
        await AssertInvalid(new LookupReadOperationsRequest { Operation = "catalog.item00", ContractVersion = 0 });

        var unknown = await Assert.ThrowsAsync<BridgeException>(() => service.LookupReadOperationsAsync(
            new LookupReadOperationsRequest { Operation = "Catalog.item00" },
            CancellationToken.None));
        Assert.Equal(CapabilityErrorCodes.UnknownReadOperation, unknown.ErrorCode);

        var untrimmed = await Assert.ThrowsAsync<BridgeException>(() => service.LookupReadOperationsAsync(
            new LookupReadOperationsRequest { Operation = " catalog.item00" },
            CancellationToken.None));
        Assert.Equal(CapabilityErrorCodes.UnknownReadOperation, untrimmed.ErrorCode);

        var unsupported = await Assert.ThrowsAsync<BridgeException>(() => service.LookupReadOperationsAsync(
            new LookupReadOperationsRequest { Operation = "catalog.item00", ContractVersion = 9 },
            CancellationToken.None));
        Assert.Equal(CapabilityErrorCodes.ReadOperationVersionUnsupported, unsupported.ErrorCode);
        Assert.Contains("Registered versions: 1.", unsupported.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_oversized_lookup_returns_result_too_large()
    {
        var service = Service([Operation("catalog.huge", description: new string('x', ReadResultBudget.MaxCharacters))]);
        var rejected = await Assert.ThrowsAsync<BridgeException>(() =>
            service.LookupReadOperationsAsync(new LookupReadOperationsRequest(), CancellationToken.None));
        Assert.Equal(CapabilityErrorCodes.ResultTooLarge, rejected.ErrorCode);
        Assert.DoesNotContain("xxxx", rejected.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Registry_validation_failure_returns_no_service()
    {
        var service = ReadOperationComposition.TryCreate(
            [Operation("catalog.bad", version: 0)],
            "instance-a",
            new DeclarativeQueryCursorCodec(),
            new DocumentQueryRevisionService(),
            new CountingQueue());
        Assert.Null(service);
    }

    [Fact]
    public async Task Valid_registry_reaches_ready_and_supplies_the_service_without_a_document()
    {
        var events = new List<string>();
        var bridges = new RecordingBridgeFactory(events);
        var coordinator = Coordinator(new RecordingDispatcherFactory(events), bridges);

        coordinator.TryBootstrap(new LifecycleRevitRuntime("2026", "26.5.0.0"));

        Assert.Equal(AddinLifecycleState.Ready, coordinator.State);
        Assert.True(bridges.Published);
        Assert.True(bridges.LastBridge?.HasRegistration);
        var service = Assert.IsAssignableFrom<IRevitReadOperationsService>(bridges.LastReadOperations);
        var result = await service.LookupReadOperationsAsync(new LookupReadOperationsRequest(), CancellationToken.None);
        var operation = Assert.Single(result.Operations);
        Assert.Equal(ProjectUserWorksetsReadOperation.OperationId, operation.Id);
        Assert.Equal(1, operation.ContractVersion);
    }

    [Fact]
    public void Registry_validation_failure_does_not_fault_startup()
    {
        var events = new List<string>();
        var dispatchers = new RecordingDispatcherFactory(events)
        {
            Configure = dispatcher => dispatcher.ReadOperationsFactory = metadata =>
                ReadOperationComposition.TryCreate(
                    [Operation("catalog.bad", version: 0)],
                    metadata.InstanceId,
                    new DeclarativeQueryCursorCodec(),
                    new DocumentQueryRevisionService(),
                    new CountingQueue())
        };
        var bridges = new RecordingBridgeFactory(events);
        var coordinator = Coordinator(dispatchers, bridges);

        coordinator.TryBootstrap(new LifecycleRevitRuntime("2026", "26.5.0.0"));

        Assert.Equal(AddinLifecycleState.Ready, coordinator.State);
        Assert.NotEqual(AddinLifecycleState.Faulted, coordinator.State);
        Assert.Null(bridges.LastReadOperations);
        Assert.True(bridges.Published);
        Assert.True(bridges.LastBridge?.HasRegistration);
        Assert.Contains("readoperations.create", events);
        Assert.Contains("registration.publish", events);
    }

    private static async Task AssertInvalid(LookupReadOperationsRequest request)
    {
        var service = Service([Operation("catalog.item00")]);
        var rejected = await Assert.ThrowsAsync<BridgeException>(() =>
            service.LookupReadOperationsAsync(request, CancellationToken.None));
        Assert.Equal(CapabilityErrorCodes.InvalidReadOperationLookup, rejected.ErrorCode);
    }

    private static IRevitReadOperationsService Service(IReadOnlyList<IExecutableReadOperation> operations)
    {
        return ReadOperationComposition.TryCreate(
            operations,
            "instance-a",
            new DeclarativeQueryCursorCodec(),
            new DocumentQueryRevisionService(),
            new CountingQueue()) ?? throw new InvalidOperationException("The fixture registry was rejected.");
    }

    private static AddinLifecycleCoordinator Coordinator(
        ILifecycleDispatcherFactory dispatchers,
        ILifecycleBridgeFactory bridges)
    {
        var coordinator = new AddinLifecycleCoordinator(
            dispatchers,
            bridges,
            new FixedMetadataSource(),
            startupTimeout: TimeSpan.FromSeconds(2),
            shutdownTimeout: TimeSpan.FromSeconds(2));
        coordinator.Prepare(new RecordingBootstrapSubscription());
        return coordinator;
    }

    private static FixtureOperation Operation(
        string id,
        int version = 1,
        string title = "Title",
        string description = "Description")
    {
        return new FixtureOperation(new ReadOperationDescriptor
        {
            Id = id,
            ContractVersion = version,
            Title = title,
            Description = description,
            Context = "document",
            Effects = "read",
            Pagination = "none",
            InputSchema = Parse(ClosedObject),
            OutputSchema = Parse(ClosedObject)
        });
    }

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private sealed class FixtureOperation(ReadOperationDescriptor descriptor) : IExecutableReadOperation
    {
        public ReadOperationDescriptor Descriptor { get; } = descriptor;

        public object Bind(JsonElement inputs) => throw new InvalidOperationException("Lookup fixtures do not execute.");

        public void ValidateInputs(object request) => throw new InvalidOperationException("Lookup fixtures do not execute.");

        public byte[] Fingerprint(object request, string documentId, ReadOperationDescriptor descriptor)
            => throw new InvalidOperationException("Lookup fixtures do not execute.");

        public ReadOperationExecutionResult Execute(object request, ReadOperationExecutionContext context)
            => throw new InvalidOperationException("Lookup fixtures do not execute.");

        public void ValidateResult(object result) => throw new InvalidOperationException("Lookup fixtures do not execute.");
    }

    private sealed class CountingQueue : IReadOperationWorkQueue
    {
        public int Calls { get; private set; }

        public Task<ReadOperationResult> EnqueueAsync(
            Func<IReadOperationDocument, ReadOperationResult> execute,
            CancellationToken cancellationToken)
        {
            _ = execute;
            Calls++;
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("This lookup test must not enqueue.");
        }
    }
}
