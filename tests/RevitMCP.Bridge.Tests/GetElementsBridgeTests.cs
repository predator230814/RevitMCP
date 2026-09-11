using System.Diagnostics;
using System.Text.Json;
using RevitMCP.Contracts;
using Xunit;

namespace RevitMCP.Bridge.Tests;

public sealed class GetElementsBridgeTests
{
    [Fact]
    public void Full_composition_advertises_protocol_v4()
    {
        var advertised = NamedPipeBridgeHost.WithAdvertisedProtocol(
            TestSupport.CreateMetadata(),
            new FakeCapabilityService(),
            new FakeQueryElementsService(),
            new FakeGetElementsService());

        Assert.Equal(BridgeProtocol.SupportedVersions, advertised.SupportedProtocolVersions);
        Assert.Equal(4, advertised.SupportedProtocolVersions.Max());
    }

    [Fact]
    public void Context_plus_query_host_stays_on_protocol_v3()
    {
        var advertised = NamedPipeBridgeHost.WithAdvertisedProtocol(
            TestSupport.CreateMetadata(),
            new FakeCapabilityService(),
            new FakeQueryElementsService());

        Assert.Equal(BridgeProtocol.QueryElementsVersions, advertised.SupportedProtocolVersions);
        Assert.DoesNotContain(4, advertised.SupportedProtocolVersions);
    }

    [Fact]
    public void Context_only_host_stays_on_protocol_v2()
    {
        var advertised = NamedPipeBridgeHost.WithAdvertisedProtocol(
            TestSupport.CreateMetadata(),
            new FakeCapabilityService());

        Assert.Equal(BridgeProtocol.GetContextVersions, advertised.SupportedProtocolVersions);
    }

    [Fact]
    public void Handshake_only_host_stays_on_protocol_v1()
    {
        var advertised = NamedPipeBridgeHost.WithAdvertisedProtocol(
            TestSupport.CreateMetadata(),
            capability: null);

        Assert.Equal(BridgeProtocol.HandshakeOnlyVersions, advertised.SupportedProtocolVersions);
    }

    [Fact]
    public void Get_elements_only_does_not_advertise_v4()
    {
        var advertised = NamedPipeBridgeHost.WithAdvertisedProtocol(
            TestSupport.CreateMetadata(),
            capability: null,
            query: null,
            getElements: new FakeGetElementsService());

        Assert.Equal(BridgeProtocol.HandshakeOnlyVersions, advertised.SupportedProtocolVersions);
        Assert.DoesNotContain(4, advertised.SupportedProtocolVersions);
    }

    [Fact]
    public void Context_plus_get_elements_without_query_advertises_only_v2()
    {
        var advertised = NamedPipeBridgeHost.WithAdvertisedProtocol(
            TestSupport.CreateMetadata(),
            new FakeCapabilityService(),
            query: null,
            getElements: new FakeGetElementsService());

        Assert.Equal(BridgeProtocol.GetContextVersions, advertised.SupportedProtocolVersions);
        Assert.DoesNotContain(3, advertised.SupportedProtocolVersions);
        Assert.DoesNotContain(4, advertised.SupportedProtocolVersions);
    }

    [Fact]
    public void Query_plus_get_elements_without_context_advertises_only_v1()
    {
        var advertised = NamedPipeBridgeHost.WithAdvertisedProtocol(
            TestSupport.CreateMetadata(),
            capability: null,
            query: new FakeQueryElementsService(),
            getElements: new FakeGetElementsService());

        Assert.Equal(BridgeProtocol.HandshakeOnlyVersions, advertised.SupportedProtocolVersions);
    }

    [Fact]
    public async Task Registration_highest_protocol_is_4_only_when_fully_capable()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        await using var full = await StartHostAsync(
            new FakeCapabilityService(),
            new FakeQueryElementsService(),
            new FakeGetElementsService());
        await using var query = await StartHostAsync(new FakeCapabilityService(), new FakeQueryElementsService());
        await using var context = await StartHostAsync(new FakeCapabilityService());
        await using var handshake = await StartHostAsync();
        await using var getOnly = await StartHostAsync(getElements: new FakeGetElementsService());

        Assert.Equal(4, full.Host.Registration?.BridgeProtocolVersion);
        Assert.Equal(3, query.Host.Registration?.BridgeProtocolVersion);
        Assert.Equal(2, context.Host.Registration?.BridgeProtocolVersion);
        Assert.Equal(1, handshake.Host.Registration?.BridgeProtocolVersion);
        Assert.Equal(1, getOnly.Host.Registration?.BridgeProtocolVersion);
    }

    [Fact]
    public void Unknown_v5_does_not_allow_any_current_capability()
    {
        Assert.False(BridgeProtocol.SupportsGetContext(5));
        Assert.False(BridgeProtocol.SupportsQueryElements(5));
        Assert.False(BridgeProtocol.SupportsGetElements(5));
    }

    [Fact]
    public async Task V4_handshake_selects_4_when_both_peers_support_full_set()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        await using var context = await StartV4HostAsync();
        await using var client = await NamedPipeBridgeClient.ConnectAsync(context.Host.PipeName, TimeSpan.FromSeconds(3), CancellationToken.None);

        var result = await client.HandshakeAsync(CreateHandshakeRequest(context.Metadata.InstanceId, [4, 3, 2, 1]), CancellationToken.None);

        Assert.Equal(4, result.SelectedProtocolVersion);
        Assert.Equal(new[] { 4, 3, 2, 1 }, result.SupportedProtocolVersions);
    }

    [Fact]
    public async Task Get_context_is_allowed_after_v4()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var capability = new FakeCapabilityService { Result = FakeCapabilityService.CreateZeroDocumentResult() };
        await using var context = await StartV4HostAsync(capability: capability);
        await using var client = await ConnectAndHandshakeAsync(context, [4, 3, 2, 1]);

        var result = await client.GetContextAsync(new GetContextRequest(), TimeSpan.FromSeconds(3), CancellationToken.None);

        Assert.Equal(1, capability.InvokeCount);
        Assert.Null(result.Document);
    }

    [Fact]
    public async Task Query_is_allowed_after_v4()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var query = new FakeQueryElementsService { Result = FakeQueryElementsService.CreateBoundedResult() };
        await using var context = await StartV4HostAsync(query: query);
        await using var client = await ConnectAndHandshakeAsync(context, [4, 3, 2, 1]);

        var result = await client.QueryElementsAsync(FakeQueryElementsService.CreateValidRequest(), TimeSpan.FromSeconds(3), CancellationToken.None);

        Assert.Equal(1, query.InvokeCount);
        Assert.Equal(2, result.MatchedCount);
    }

    [Theory]
    [InlineData(new[] { 1 })]
    [InlineData(new[] { 2, 1 })]
    [InlineData(new[] { 3, 2, 1 })]
    public async Task Typed_get_elements_is_rejected_before_v4(int[] versions)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var getElements = new FakeGetElementsService();
        await using var context = await StartV4HostAsync(getElements: getElements);
        await using var client = await ConnectAndHandshakeAsync(context, versions);

        var exception = await Assert.ThrowsAsync<BridgeException>(() =>
            client.GetElementsAsync(FakeGetElementsService.CreateValidRequest(), TimeSpan.FromSeconds(3), CancellationToken.None));

        Assert.Equal(BridgeErrorCodes.ProtocolIncompatible, exception.ErrorCode);
        Assert.Equal(0, getElements.InvokeCount);
    }

    [Fact]
    public async Task Typed_get_elements_is_allowed_after_v4()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var getElements = new FakeGetElementsService { Result = FakeGetElementsService.CreateOkResult() };
        await using var context = await StartV4HostAsync(getElements: getElements);
        await using var client = await ConnectAndHandshakeAsync(context, [4, 3, 2, 1]);

        var result = await client.GetElementsAsync(FakeGetElementsService.CreateValidRequest(), TimeSpan.FromSeconds(3), CancellationToken.None);

        Assert.Equal(1, getElements.InvokeCount);
        Assert.Equal(GetElementResultStatus.Ok, Assert.Single(result.Elements).Status);
    }

    [Fact]
    public async Task Named_pipe_get_elements_returns_capability_result()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var expected = FakeGetElementsService.CreateOkResult();
        var getElements = new FakeGetElementsService { Result = expected };
        await using var context = await StartV4HostAsync(getElements: getElements);
        await using var client = await ConnectAndHandshakeAsync(context, [4, 3, 2, 1]);
        var request = FakeGetElementsService.CreateValidRequest();

        var result = await client.GetElementsAsync(request, TimeSpan.FromSeconds(3), CancellationToken.None);

        Assert.Equal(1, getElements.InvokeCount);
        Assert.Equal(request.DocumentId, getElements.LastRequest!.DocumentId);
        Assert.Equal(expected.Context.DocumentId, result.Context.DocumentId);
        Assert.Equal(expected.Elements[0].ElementRef, result.Elements[0].ElementRef);
        Assert.Equal(expected.Elements[0].Name.Value, result.Elements[0].Name.Value);
    }

    [Fact]
    public async Task Not_found_survives_named_pipe_round_trip()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var getElements = new FakeGetElementsService { Result = FakeGetElementsService.CreatePartialResult() };
        await using var context = await StartV4HostAsync(getElements: getElements);
        await using var client = await ConnectAndHandshakeAsync(context, [4, 3, 2, 1]);

        var result = await client.GetElementsAsync(
            new GetElementsRequest
            {
                DocumentId = "opaque-document-id",
                ElementRefs = ["ref-a", "not-a-revit-element-ref"],
                Projection = new GetElementsProjection { Fields = [GetElementField.Name] }
            },
            TimeSpan.FromSeconds(3),
            CancellationToken.None);

        Assert.Equal(2, result.Elements.Count);
        Assert.Equal(GetElementResultStatus.Ok, result.Elements[0].Status);
        Assert.Equal("ref-a", result.Elements[0].ElementRef);
        Assert.Equal(GetElementResultStatus.NotFound, result.Elements[1].Status);
        Assert.Equal("not-a-revit-element-ref", result.Elements[1].ElementRef);
        Assert.False(result.Elements[1].Name.IsRequested);
    }

    [Fact]
    public async Task Tri_state_fields_survive_named_pipe_round_trip()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var getElements = new FakeGetElementsService { Result = FakeGetElementsService.CreateTriStateResult() };
        await using var context = await StartV4HostAsync(getElements: getElements);
        await using var client = await ConnectAndHandshakeAsync(context, [4, 3, 2, 1]);

        var result = await client.GetElementsAsync(FakeGetElementsService.CreateValidRequest(), TimeSpan.FromSeconds(3), CancellationToken.None);
        var json = JsonSerializer.Serialize(result, ContractJson.Options);
        using var document = JsonDocument.Parse(json);
        var element = document.RootElement.GetProperty("elements")[0];

        Assert.Equal("VAV Box 12", result.Elements[0].Name.Value);
        Assert.True(result.Elements[0].CategoryName.IsRequested);
        Assert.Null(result.Elements[0].CategoryName.Value);
        Assert.False(result.Elements[0].FamilyName.IsRequested);
        Assert.True(element.TryGetProperty("name", out _));
        Assert.Equal(JsonValueKind.Null, element.GetProperty("category_name").ValueKind);
        Assert.False(element.TryGetProperty("family_name", out _));
        Assert.False(element.TryGetProperty("type_name", out _));
        Assert.False(element.TryGetProperty("level_name", out _));
    }

    [Fact]
    public async Task Parameter_entries_survive_named_pipe_round_trip()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var getElements = new FakeGetElementsService { Result = FakeGetElementsService.CreateParameterResult() };
        await using var context = await StartV4HostAsync(getElements: getElements);
        await using var client = await ConnectAndHandshakeAsync(context, [4, 3, 2, 1]);

        var result = await client.GetElementsAsync(FakeGetElementsService.CreateValidRequest(), TimeSpan.FromSeconds(3), CancellationToken.None);
        var parameters = result.Elements[0].Parameters!;

        Assert.Equal(2, parameters.Count);
        Assert.Equal("Flow", parameters[0].Name);
        Assert.Equal(GetElementParameterSource.Instance, parameters[0].Source);
        Assert.Equal("850 CFM", parameters[0].ValueText);
        Assert.Equal("Type Mark", parameters[1].Name);
        Assert.Equal(GetElementParameterSource.Type, parameters[1].Source);
        Assert.Null(parameters[1].ValueText);
        Assert.False(result.Elements[0].ParametersTruncated);
    }

    [Fact]
    public async Task Get_elements_capability_error_survives_streamjsonrpc_mapping()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var getElements = new FakeGetElementsService
        {
            Error = new BridgeException(CapabilityErrorCodes.InvalidInspection, "The element inspection request is invalid.")
        };
        await using var context = await StartV4HostAsync(getElements: getElements);
        await using var client = await ConnectAndHandshakeAsync(context, [4, 3, 2, 1]);

        var exception = await Assert.ThrowsAsync<BridgeException>(() =>
            client.GetElementsAsync(FakeGetElementsService.CreateValidRequest(), TimeSpan.FromSeconds(3), CancellationToken.None));

        Assert.Equal(CapabilityErrorCodes.InvalidInspection, exception.ErrorCode);
        Assert.Equal("The element inspection request is invalid.", exception.Message);
        Assert.DoesNotContain("stack", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Silent_get_elements_peer_times_out_as_execution_timeout()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var getElements = new FakeGetElementsService
        {
            Hold = new TaskCompletionSource<GetElementsResult>(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        await using var context = await StartV4HostAsync(getElements: getElements);
        await using var client = await ConnectAndHandshakeAsync(context, [4, 3, 2, 1]);

        var exception = await Assert.ThrowsAsync<BridgeException>(() =>
            client.GetElementsAsync(FakeGetElementsService.CreateValidRequest(), TimeSpan.FromMilliseconds(400), CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.Equal(CapabilityErrorCodes.ExecutionTimeout, exception.ErrorCode);

        var reuse = await Assert.ThrowsAsync<BridgeException>(() =>
            client.GetElementsAsync(FakeGetElementsService.CreateValidRequest(), TimeSpan.FromSeconds(3), CancellationToken.None));
        Assert.Equal(CapabilityErrorCodes.ExecutionFailed, reuse.ErrorCode);
        Assert.Equal(1, getElements.InvokeCount);

        getElements.Hold.TrySetCanceled();
    }

    [Fact]
    public async Task Get_elements_timeout_cancels_the_server_request_token()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var getElements = new FakeGetElementsService
        {
            Hold = new TaskCompletionSource<GetElementsResult>(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        await using var context = await StartV4HostAsync(getElements: getElements);
        await using var client = await ConnectAndHandshakeAsync(context, [4, 3, 2, 1]);

        var exception = await Assert.ThrowsAsync<BridgeException>(() =>
            client.GetElementsAsync(FakeGetElementsService.CreateValidRequest(), TimeSpan.FromMilliseconds(400), CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.Equal(CapabilityErrorCodes.ExecutionTimeout, exception.ErrorCode);
        await getElements.RequestTokenCancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(getElements.RequestTokenCancelled.Task.IsCompletedSuccessfully);
        Assert.False(getElements.Hold!.Task.IsCompleted);
    }

    [Fact]
    public async Task Get_elements_caller_cancellation_remains_cancellation()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var getElements = new FakeGetElementsService
        {
            Hold = new TaskCompletionSource<GetElementsResult>(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        await using var context = await StartV4HostAsync(getElements: getElements);
        await using var client = await ConnectAndHandshakeAsync(context, [4, 3, 2, 1]);
        using var cts = new CancellationTokenSource();
        var pending = client.GetElementsAsync(FakeGetElementsService.CreateValidRequest(), TimeSpan.FromSeconds(10), cts.Token);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(5)));
        getElements.Hold.TrySetCanceled();
    }

    private static BridgeHandshakeRequest CreateHandshakeRequest(string instanceId, IReadOnlyList<int> versions)
    {
        return new BridgeHandshakeRequest
        {
            ExpectedInstanceId = instanceId,
            SupportedProtocolVersions = versions,
            ClientName = "RevitMCP.Tests"
        };
    }

    private static async Task<NamedPipeBridgeClient> ConnectAndHandshakeAsync(HostContext context, IReadOnlyList<int> versions)
    {
        var client = await NamedPipeBridgeClient.ConnectAsync(context.Host.PipeName, TimeSpan.FromSeconds(3), CancellationToken.None);
        await client.HandshakeAsync(CreateHandshakeRequest(context.Metadata.InstanceId, versions), CancellationToken.None);
        return client;
    }

    private static Task<HostContext> StartV4HostAsync(
        IRevitCapabilityService? capability = null,
        IRevitQueryElementsService? query = null,
        IRevitGetElementsService? getElements = null)
    {
        return StartHostAsync(
            capability ?? new FakeCapabilityService(),
            query ?? new FakeQueryElementsService(),
            getElements ?? new FakeGetElementsService());
    }

    private static async Task<HostContext> StartHostAsync(
        IRevitCapabilityService? capability = null,
        IRevitQueryElementsService? query = null,
        IRevitGetElementsService? getElements = null,
        BridgeInstanceMetadata? metadata = null)
    {
        metadata ??= CreateLiveMetadata();
        var root = Path.Combine(Path.GetTempPath(), "RevitMCP.Tests", Guid.NewGuid().ToString("N"));
        var store = new FileRegistrationStore(root);
        var host = await NamedPipeBridgeHost.StartAsync(metadata, store, capability, query, getElements, CancellationToken.None);
        return new HostContext(host, metadata);
    }

    private static BridgeInstanceMetadata CreateLiveMetadata()
    {
        var process = Process.GetCurrentProcess();
        return TestSupport.CreateMetadata(
            processId: process.Id,
            startTime: new DateTimeOffset(process.StartTime).ToUniversalTime(),
            sessionId: process.SessionId);
    }

    private sealed class HostContext : IAsyncDisposable
    {
        public HostContext(NamedPipeBridgeHost host, BridgeInstanceMetadata metadata)
        {
            Host = host;
            Metadata = metadata;
        }

        public NamedPipeBridgeHost Host { get; }

        public BridgeInstanceMetadata Metadata { get; }

        public ValueTask DisposeAsync() => Host.DisposeAsync();
    }
}
