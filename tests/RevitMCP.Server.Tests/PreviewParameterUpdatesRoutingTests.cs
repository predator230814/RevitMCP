using RevitMCP.Contracts;
using RevitMCP.Server;
using Xunit;

namespace RevitMCP.Server.Tests;

public sealed class PreviewParameterUpdatesRoutingTests
{
    [Fact]
    public void Preview_eligibility_is_protocol_v8_only()
    {
        Assert.False(InstanceTargetResolver.IsPreviewParameterUpdatesEligible(TestSupport.Ready("v1", "pipe-v1", protocolVersion: 1)));
        Assert.False(InstanceTargetResolver.IsPreviewParameterUpdatesEligible(TestSupport.Ready("v2", "pipe-v2", protocolVersion: 2)));
        Assert.False(InstanceTargetResolver.IsPreviewParameterUpdatesEligible(TestSupport.Ready("v3", "pipe-v3", protocolVersion: 3)));
        Assert.False(InstanceTargetResolver.IsPreviewParameterUpdatesEligible(TestSupport.Ready("v4", "pipe-v4", protocolVersion: 4)));
        Assert.False(InstanceTargetResolver.IsPreviewParameterUpdatesEligible(TestSupport.Ready("v5", "pipe-v5", protocolVersion: 5)));
        Assert.False(InstanceTargetResolver.IsPreviewParameterUpdatesEligible(TestSupport.Ready("v6", "pipe-v6", protocolVersion: 6)));
        Assert.False(InstanceTargetResolver.IsPreviewParameterUpdatesEligible(TestSupport.Ready("v7", "pipe-v7", protocolVersion: 7)));
        Assert.True(InstanceTargetResolver.IsPreviewParameterUpdatesEligible(TestSupport.Ready("v8", "pipe-v8", protocolVersion: 8)));
        Assert.True(InstanceTargetResolver.IsPreviewParameterUpdatesEligible(TestSupport.Ready("v9", "pipe-v9", protocolVersion: 9)));
        Assert.False(InstanceTargetResolver.IsPreviewParameterUpdatesEligible(TestSupport.Ready("v10", "pipe-v10", protocolVersion: 10)));
    }

    [Fact]
    public void Existing_six_capability_predicates_remain_true_on_v8()
    {
        var v8 = TestSupport.Ready("v8", "pipe-v8", protocolVersion: 8);
        Assert.True(InstanceTargetResolver.IsGetContextEligible(v8));
        Assert.True(InstanceTargetResolver.IsQueryElementsEligible(v8));
        Assert.True(InstanceTargetResolver.IsGetElementsEligible(v8));
        Assert.True(InstanceTargetResolver.IsDescribeParametersEligible(v8));
        Assert.True(InstanceTargetResolver.IsGetParameterValuesEligible(v8));
        Assert.True(InstanceTargetResolver.IsGetMepTopologyEligible(v8));
        Assert.True(InstanceTargetResolver.IsPreviewParameterUpdatesEligible(v8));
    }

    [Fact]
    public async Task Zero_preview_capable_instances_returns_no_revit_instance()
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("v7", "pipe-v7", protocolVersion: 7));
        var factory = UnusedFactory();
        var outcome = await CreateService(discovery, factory).ExecuteAsync(null, TestSupport.CreatePreviewParameterUpdatesRequest(), CancellationToken.None);

        Assert.Equal(McpToolErrorCodes.NoRevitInstance, outcome.ErrorCode);
        Assert.Empty(factory.RequestedPipes);
    }

    [Fact]
    public async Task Exactly_one_preview_capable_instance_is_auto_selected()
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("v7", "pipe-v7", protocolVersion: 7));
        discovery.Instances.Add(TestSupport.Ready("only", "pipe-only", protocolVersion: 8));
        var factory = SuccessfulFactory("only", "pipe-only");
        var outcome = await CreateService(discovery, factory).ExecuteAsync(null, TestSupport.CreatePreviewParameterUpdatesRequest(), CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        Assert.Equal("only", outcome.Result!.Context.InstanceId);
        Assert.Equal(new[] { "pipe-only" }, factory.RequestedPipes);
    }

    [Fact]
    public async Task Multiple_preview_capable_instances_require_instance_id()
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("beta", "pipe-beta", protocolVersion: 8));
        discovery.Instances.Add(TestSupport.Ready("alpha", "pipe-alpha", protocolVersion: 8));
        var factory = UnusedFactory();
        var outcome = await CreateService(discovery, factory).ExecuteAsync(null, TestSupport.CreatePreviewParameterUpdatesRequest(), CancellationToken.None);

        Assert.Equal(McpToolErrorCodes.InstanceRequired, outcome.ErrorCode);
        Assert.Equal(new[] { "alpha", "beta" }, outcome.Candidates!.Select(candidate => candidate.InstanceId));
        Assert.Empty(factory.RequestedPipes);
    }

    [Fact]
    public async Task Explicit_unknown_id_returns_not_found()
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("alpha", "pipe-alpha", protocolVersion: 8));
        var factory = UnusedFactory();
        var outcome = await CreateService(discovery, factory).ExecuteAsync("missing", TestSupport.CreatePreviewParameterUpdatesRequest(), CancellationToken.None);

        Assert.Equal(McpToolErrorCodes.InstanceNotFound, outcome.ErrorCode);
        Assert.Empty(factory.RequestedPipes);
    }

    [Theory]
    [InlineData("v7", 7)]
    [InlineData("v10", 10)]
    public async Task Explicit_ineligible_id_returns_unavailable(string instanceId, int protocolVersion)
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready(instanceId, "pipe-" + instanceId, protocolVersion: protocolVersion));
        discovery.Instances.Add(TestSupport.Ready("v8", "pipe-v8", protocolVersion: 8));
        var factory = UnusedFactory();
        var outcome = await CreateService(discovery, factory).ExecuteAsync(instanceId, TestSupport.CreatePreviewParameterUpdatesRequest(), CancellationToken.None);

        Assert.Equal(McpToolErrorCodes.InstanceUnavailable, outcome.ErrorCode);
        Assert.Empty(factory.RequestedPipes);
    }

    [Fact]
    public async Task Explicit_v8_id_is_selected()
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("v7", "pipe-v7", protocolVersion: 7));
        discovery.Instances.Add(TestSupport.Ready("v8", "pipe-v8", protocolVersion: 8));
        var factory = SuccessfulFactory("v8", "pipe-v8");
        var outcome = await CreateService(discovery, factory).ExecuteAsync("v8", TestSupport.CreatePreviewParameterUpdatesRequest(), CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        Assert.Equal(new[] { "pipe-v8" }, factory.RequestedPipes);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public async Task Explicit_empty_or_whitespace_id_never_auto_selects(string instanceId)
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("only", "pipe-only", protocolVersion: 8));
        var factory = UnusedFactory();
        var outcome = await CreateService(discovery, factory).ExecuteAsync(instanceId, TestSupport.CreatePreviewParameterUpdatesRequest(), CancellationToken.None);

        Assert.Equal(McpToolErrorCodes.InstanceNotFound, outcome.ErrorCode);
        Assert.Empty(factory.RequestedPipes);
    }

    [Fact]
    public async Task Each_invocation_performs_fresh_discovery_and_creates_a_fresh_client()
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("only", "pipe-only", protocolVersion: 8));
        var factory = SuccessfulFactory("only", "pipe-only");
        var service = CreateService(discovery, factory);

        await service.ExecuteAsync(null, TestSupport.CreatePreviewParameterUpdatesRequest(), CancellationToken.None);
        await service.ExecuteAsync(null, TestSupport.CreatePreviewParameterUpdatesRequest(), CancellationToken.None);

        Assert.Equal(2, discovery.CallCount);
        Assert.Equal(2, factory.Clients.Count);
        Assert.All(factory.Clients, client => Assert.True(client.Disposed));
    }

    private static PreviewParameterUpdatesApplicationService CreateService(
        FakeDiscovery discovery,
        RecordingBridgeClientFactory factory)
    {
        return new PreviewParameterUpdatesApplicationService(discovery, factory, new ServerTimeouts
        {
            BootstrapTimeout = TimeSpan.FromMilliseconds(50),
            CapabilityTimeout = TimeSpan.FromMilliseconds(75)
        });
    }

    private static RecordingBridgeClientFactory UnusedFactory()
    {
        return new RecordingBridgeClientFactory
        {
            Connect = (_, _, _) => throw new InvalidOperationException("Bridge invocation should not occur.")
        };
    }

    private static RecordingBridgeClientFactory SuccessfulFactory(string instanceId, string expectedPipe)
    {
        return new RecordingBridgeClientFactory
        {
            Connect = (pipe, _, _) =>
            {
                Assert.Equal(expectedPipe, pipe);
                var registration = TestSupport.CreateRegistration(instanceId, expectedPipe);
                var client = new RecordingBridgeClient
                {
                    Handshake = (_, _) => Task.FromResult(TestSupport.CreateHandshake(registration, selectedProtocolVersion: 8)),
                    PreviewParameterUpdates = (request, _, _) => Task.FromResult(
                        TestSupport.CreatePreviewParameterUpdatesResult(
                            instanceId,
                            request.DocumentId,
                            ready: false,
                            [TestSupport.CreatePreviewFailureItem(request.Updates[0].ElementRef, request.Updates[0].ParameterRef, PreviewParameterUpdateStatus.ElementNotFound)]))
                };
                return Task.FromResult(client);
            }
        };
    }
}
