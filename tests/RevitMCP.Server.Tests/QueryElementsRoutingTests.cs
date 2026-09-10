using RevitMCP.Contracts;
using RevitMCP.Server;
using Xunit;

namespace RevitMCP.Server.Tests;

public sealed class QueryElementsRoutingTests
{
    [Fact]
    public void Query_eligibility_is_an_explicit_protocol_set()
    {
        Assert.False(InstanceTargetResolver.IsQueryElementsEligible(TestSupport.Ready("v1", "pipe-v1", protocolVersion: 1)));
        Assert.False(InstanceTargetResolver.IsQueryElementsEligible(TestSupport.Ready("v2", "pipe-v2", protocolVersion: 2)));
        Assert.True(InstanceTargetResolver.IsQueryElementsEligible(TestSupport.Ready("v3", "pipe-v3", protocolVersion: 3)));
        Assert.False(InstanceTargetResolver.IsQueryElementsEligible(TestSupport.Ready("v4", "pipe-v4", protocolVersion: 4)));
    }

    [Fact]
    public void Get_context_eligibility_remains_explicit_after_query_routing()
    {
        Assert.True(InstanceTargetResolver.IsGetContextEligible(TestSupport.Ready("v2", "pipe-v2", protocolVersion: 2)));
        Assert.True(InstanceTargetResolver.IsGetContextEligible(TestSupport.Ready("v3", "pipe-v3", protocolVersion: 3)));
        Assert.False(InstanceTargetResolver.IsGetContextEligible(TestSupport.Ready("v1", "pipe-v1", protocolVersion: 1)));
        Assert.False(InstanceTargetResolver.IsGetContextEligible(TestSupport.Ready("v4", "pipe-v4", protocolVersion: 4)));
    }

    [Fact]
    public async Task Zero_query_capable_instances_returns_no_revit_instance()
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("v2", "pipe-v2", protocolVersion: 2));
        var factory = UnusedFactory();
        var outcome = await CreateService(discovery, factory).ExecuteAsync(null, TestSupport.CreateQueryRequest(), CancellationToken.None);

        Assert.Equal(McpToolErrorCodes.NoRevitInstance, outcome.ErrorCode);
        Assert.Empty(factory.RequestedPipes);
    }

    [Fact]
    public async Task Exactly_one_query_capable_instance_is_auto_selected()
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("v2", "pipe-v2", protocolVersion: 2));
        discovery.Instances.Add(TestSupport.Ready("only", "pipe-only", protocolVersion: 3));
        var factory = SuccessfulFactory("only", "pipe-only");
        var outcome = await CreateService(discovery, factory).ExecuteAsync(null, TestSupport.CreateQueryRequest(), CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        Assert.Equal("only", outcome.Result!.Context.InstanceId);
        Assert.Equal(new[] { "pipe-only" }, factory.RequestedPipes);
    }

    [Fact]
    public async Task Multiple_query_capable_instances_require_instance_id()
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("beta", "pipe-beta", protocolVersion: 3));
        discovery.Instances.Add(TestSupport.Ready("alpha", "pipe-alpha", protocolVersion: 3));
        var factory = UnusedFactory();
        var outcome = await CreateService(discovery, factory).ExecuteAsync(null, TestSupport.CreateQueryRequest(), CancellationToken.None);

        Assert.Equal(McpToolErrorCodes.InstanceRequired, outcome.ErrorCode);
        Assert.Equal(new[] { "alpha", "beta" }, outcome.Candidates!.Select(candidate => candidate.InstanceId));
        Assert.Empty(factory.RequestedPipes);
    }

    [Fact]
    public void Ambiguity_candidates_are_sorted_by_opaque_instance_id()
    {
        var discovered = new[]
        {
            TestSupport.Ready("m", "pipe-m", protocolVersion: 3),
            TestSupport.Ready("a", "pipe-a", protocolVersion: 3),
            TestSupport.Ready("z", "pipe-z", protocolVersion: 3)
        };

        var resolution = InstanceTargetResolver.ResolveForQueryElements(discovered, instanceId: null);

        Assert.Equal(McpToolErrorCodes.InstanceRequired, resolution.ErrorCode);
        Assert.Equal(new[] { "a", "m", "z" }, resolution.Candidates!.Select(candidate => candidate.InstanceId));
    }

    [Fact]
    public async Task Explicit_unknown_id_returns_not_found()
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("alpha", "pipe-alpha", protocolVersion: 3));
        var factory = UnusedFactory();
        var outcome = await CreateService(discovery, factory).ExecuteAsync("missing", TestSupport.CreateQueryRequest(), CancellationToken.None);

        Assert.Equal(McpToolErrorCodes.InstanceNotFound, outcome.ErrorCode);
        Assert.Empty(factory.RequestedPipes);
    }

    [Fact]
    public async Task Explicit_v2_id_returns_unavailable()
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("v2", "pipe-v2", protocolVersion: 2));
        discovery.Instances.Add(TestSupport.Ready("v3", "pipe-v3", protocolVersion: 3));
        var factory = UnusedFactory();
        var outcome = await CreateService(discovery, factory).ExecuteAsync("v2", TestSupport.CreateQueryRequest(), CancellationToken.None);

        Assert.Equal(McpToolErrorCodes.InstanceUnavailable, outcome.ErrorCode);
        Assert.Empty(factory.RequestedPipes);
    }

    [Fact]
    public async Task Explicit_v3_id_is_selected()
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("v2", "pipe-v2", protocolVersion: 2));
        discovery.Instances.Add(TestSupport.Ready("v3", "pipe-v3", protocolVersion: 3));
        var factory = SuccessfulFactory("v3", "pipe-v3");
        var outcome = await CreateService(discovery, factory).ExecuteAsync("v3", TestSupport.CreateQueryRequest(), CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        Assert.Equal(new[] { "pipe-v3" }, factory.RequestedPipes);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public async Task Explicit_empty_or_whitespace_id_never_auto_selects(string instanceId)
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("only", "pipe-only", protocolVersion: 3));
        var factory = UnusedFactory();
        var outcome = await CreateService(discovery, factory).ExecuteAsync(instanceId, TestSupport.CreateQueryRequest(), CancellationToken.None);

        Assert.Equal(McpToolErrorCodes.InstanceNotFound, outcome.ErrorCode);
        Assert.Empty(factory.RequestedPipes);
    }

    [Fact]
    public async Task Each_invocation_performs_fresh_discovery()
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("only", "pipe-only", protocolVersion: 3));
        var factory = SuccessfulFactory("only", "pipe-only");
        var service = CreateService(discovery, factory);

        await service.ExecuteAsync(null, TestSupport.CreateQueryRequest(), CancellationToken.None);
        await service.ExecuteAsync(null, TestSupport.CreateQueryRequest(), CancellationToken.None);

        Assert.Equal(2, discovery.CallCount);
    }

    private static QueryElementsApplicationService CreateService(
        FakeDiscovery discovery,
        RecordingBridgeClientFactory factory)
    {
        return new QueryElementsApplicationService(discovery, factory, new ServerTimeouts
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
                    Handshake = (request, _) => Task.FromResult(TestSupport.CreateHandshake(registration, selectedProtocolVersion: 3)),
                    QueryElements = (_, _, _) => Task.FromResult(TestSupport.CreateQueryResult(instanceId, "doc", 0, false))
                };
                return Task.FromResult(client);
            }
        };
    }
}
