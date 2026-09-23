using RevitMCP.Contracts;
using RevitMCP.Server;
using Xunit;

namespace RevitMCP.Server.Tests;

public sealed class GetMepTopologyRoutingTests
{
    [Fact]
    public void Get_mep_topology_eligibility_is_protocol_v7_only()
    {
        Assert.False(InstanceTargetResolver.IsGetMepTopologyEligible(TestSupport.Ready("v1", "pipe-v1", protocolVersion: 1)));
        Assert.False(InstanceTargetResolver.IsGetMepTopologyEligible(TestSupport.Ready("v2", "pipe-v2", protocolVersion: 2)));
        Assert.False(InstanceTargetResolver.IsGetMepTopologyEligible(TestSupport.Ready("v3", "pipe-v3", protocolVersion: 3)));
        Assert.False(InstanceTargetResolver.IsGetMepTopologyEligible(TestSupport.Ready("v4", "pipe-v4", protocolVersion: 4)));
        Assert.False(InstanceTargetResolver.IsGetMepTopologyEligible(TestSupport.Ready("v5", "pipe-v5", protocolVersion: 5)));
        Assert.False(InstanceTargetResolver.IsGetMepTopologyEligible(TestSupport.Ready("v6", "pipe-v6", protocolVersion: 6)));
        Assert.True(InstanceTargetResolver.IsGetMepTopologyEligible(TestSupport.Ready("v7", "pipe-v7", protocolVersion: 7)));
        Assert.False(InstanceTargetResolver.IsGetMepTopologyEligible(TestSupport.Ready("v8", "pipe-v8", protocolVersion: 8)));
    }

    [Fact]
    public void Inherited_capability_eligibility_remains_explicit_on_v7()
    {
        var v7 = TestSupport.Ready("v7", "pipe-v7", protocolVersion: 7);
        Assert.True(InstanceTargetResolver.IsGetContextEligible(v7));
        Assert.True(InstanceTargetResolver.IsQueryElementsEligible(v7));
        Assert.True(InstanceTargetResolver.IsGetElementsEligible(v7));
        Assert.True(InstanceTargetResolver.IsDescribeParametersEligible(v7));
        Assert.True(InstanceTargetResolver.IsGetParameterValuesEligible(v7));
        Assert.True(InstanceTargetResolver.IsGetMepTopologyEligible(v7));

        var v6 = TestSupport.Ready("v6", "pipe-v6", protocolVersion: 6);
        Assert.True(InstanceTargetResolver.IsGetContextEligible(v6));
        Assert.True(InstanceTargetResolver.IsQueryElementsEligible(v6));
        Assert.True(InstanceTargetResolver.IsGetElementsEligible(v6));
        Assert.True(InstanceTargetResolver.IsDescribeParametersEligible(v6));
        Assert.True(InstanceTargetResolver.IsGetParameterValuesEligible(v6));
        Assert.False(InstanceTargetResolver.IsGetMepTopologyEligible(v6));

        var v8 = TestSupport.Ready("v8", "pipe-v8", protocolVersion: 8);
        Assert.False(InstanceTargetResolver.IsGetContextEligible(v8));
        Assert.False(InstanceTargetResolver.IsQueryElementsEligible(v8));
        Assert.False(InstanceTargetResolver.IsGetElementsEligible(v8));
        Assert.False(InstanceTargetResolver.IsDescribeParametersEligible(v8));
        Assert.False(InstanceTargetResolver.IsGetParameterValuesEligible(v8));
        Assert.False(InstanceTargetResolver.IsGetMepTopologyEligible(v8));
    }

    [Fact]
    public async Task Zero_topology_capable_instances_returns_no_revit_instance()
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("v6", "pipe-v6", protocolVersion: 6));
        var factory = UnusedFactory();
        var outcome = await CreateService(discovery, factory).ExecuteAsync(null, TestSupport.CreateGetMepTopologyRequest(), CancellationToken.None);

        Assert.Equal(McpToolErrorCodes.NoRevitInstance, outcome.ErrorCode);
        Assert.Empty(factory.RequestedPipes);
    }

    [Fact]
    public async Task Exactly_one_topology_capable_instance_is_auto_selected()
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("v6", "pipe-v6", protocolVersion: 6));
        discovery.Instances.Add(TestSupport.Ready("only", "pipe-only", protocolVersion: 7));
        var factory = SuccessfulFactory("only", "pipe-only");
        var outcome = await CreateService(discovery, factory).ExecuteAsync(null, TestSupport.CreateGetMepTopologyRequest(), CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        Assert.Equal("only", outcome.Result!.Context.InstanceId);
        Assert.Equal(new[] { "pipe-only" }, factory.RequestedPipes);
    }

    [Fact]
    public async Task Multiple_topology_capable_instances_require_instance_id()
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("beta", "pipe-beta", protocolVersion: 7));
        discovery.Instances.Add(TestSupport.Ready("alpha", "pipe-alpha", protocolVersion: 7));
        var factory = UnusedFactory();
        var outcome = await CreateService(discovery, factory).ExecuteAsync(null, TestSupport.CreateGetMepTopologyRequest(), CancellationToken.None);

        Assert.Equal(McpToolErrorCodes.InstanceRequired, outcome.ErrorCode);
        Assert.Equal(new[] { "alpha", "beta" }, outcome.Candidates!.Select(candidate => candidate.InstanceId));
        Assert.Empty(factory.RequestedPipes);
    }

    [Fact]
    public void Ambiguity_candidates_are_sorted_by_opaque_instance_id()
    {
        var discovered = new[]
        {
            TestSupport.Ready("m", "pipe-m", protocolVersion: 7),
            TestSupport.Ready("a", "pipe-a", protocolVersion: 7),
            TestSupport.Ready("z", "pipe-z", protocolVersion: 7)
        };

        var resolution = InstanceTargetResolver.ResolveForGetMepTopology(discovered, instanceId: null);

        Assert.Equal(McpToolErrorCodes.InstanceRequired, resolution.ErrorCode);
        Assert.Equal(new[] { "a", "m", "z" }, resolution.Candidates!.Select(candidate => candidate.InstanceId));
    }

    [Fact]
    public async Task Explicit_unknown_id_returns_not_found()
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("alpha", "pipe-alpha", protocolVersion: 7));
        var factory = UnusedFactory();
        var outcome = await CreateService(discovery, factory).ExecuteAsync("missing", TestSupport.CreateGetMepTopologyRequest(), CancellationToken.None);

        Assert.Equal(McpToolErrorCodes.InstanceNotFound, outcome.ErrorCode);
        Assert.Empty(factory.RequestedPipes);
    }

    [Fact]
    public async Task Explicit_v6_id_returns_unavailable()
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("v6", "pipe-v6", protocolVersion: 6));
        discovery.Instances.Add(TestSupport.Ready("v7", "pipe-v7", protocolVersion: 7));
        var factory = UnusedFactory();
        var outcome = await CreateService(discovery, factory).ExecuteAsync("v6", TestSupport.CreateGetMepTopologyRequest(), CancellationToken.None);

        Assert.Equal(McpToolErrorCodes.InstanceUnavailable, outcome.ErrorCode);
        Assert.Empty(factory.RequestedPipes);
    }

    [Fact]
    public async Task Explicit_v7_id_is_selected()
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("v6", "pipe-v6", protocolVersion: 6));
        discovery.Instances.Add(TestSupport.Ready("v7", "pipe-v7", protocolVersion: 7));
        var factory = SuccessfulFactory("v7", "pipe-v7");
        var outcome = await CreateService(discovery, factory).ExecuteAsync("v7", TestSupport.CreateGetMepTopologyRequest(), CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        Assert.Equal(new[] { "pipe-v7" }, factory.RequestedPipes);
    }

    [Fact]
    public async Task Explicit_v8_id_returns_unavailable()
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("v8", "pipe-v8", protocolVersion: 8));
        var factory = UnusedFactory();
        var outcome = await CreateService(discovery, factory).ExecuteAsync("v8", TestSupport.CreateGetMepTopologyRequest(), CancellationToken.None);

        Assert.Equal(McpToolErrorCodes.InstanceUnavailable, outcome.ErrorCode);
        Assert.Empty(factory.RequestedPipes);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public async Task Explicit_empty_or_whitespace_id_never_auto_selects(string instanceId)
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("only", "pipe-only", protocolVersion: 7));
        var factory = UnusedFactory();
        var outcome = await CreateService(discovery, factory).ExecuteAsync(instanceId, TestSupport.CreateGetMepTopologyRequest(), CancellationToken.None);

        Assert.Equal(McpToolErrorCodes.InstanceNotFound, outcome.ErrorCode);
        Assert.Empty(factory.RequestedPipes);
    }

    [Fact]
    public async Task Each_invocation_performs_fresh_discovery()
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("only", "pipe-only", protocolVersion: 7));
        var factory = SuccessfulFactory("only", "pipe-only");
        var service = CreateService(discovery, factory);

        await service.ExecuteAsync(null, TestSupport.CreateGetMepTopologyRequest(), CancellationToken.None);
        await service.ExecuteAsync(null, TestSupport.CreateGetMepTopologyRequest(), CancellationToken.None);

        Assert.Equal(2, discovery.CallCount);
        Assert.Equal(2, factory.Clients.Count);
        Assert.All(factory.Clients, client => Assert.True(client.Disposed));
    }

    private static GetMepTopologyApplicationService CreateService(
        FakeDiscovery discovery,
        RecordingBridgeClientFactory factory)
    {
        return new GetMepTopologyApplicationService(discovery, factory, new ServerTimeouts
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
                    Handshake = (_, _) => Task.FromResult(TestSupport.CreateHandshake(registration, selectedProtocolVersion: 7)),
                    GetMepTopology = (_, _, _) => Task.FromResult(
                        TestSupport.CreateGetMepTopologyResult(instanceId, "doc-1"))
                };
                return Task.FromResult(client);
            }
        };
    }
}
