using RevitMCP.Contracts;
using RevitMCP.Server;
using Xunit;

namespace RevitMCP.Server.Tests;

public sealed class DescribeParametersRoutingTests
{
    [Fact]
    public void Describe_parameters_eligibility_is_protocol_v5_only()
    {
        Assert.False(InstanceTargetResolver.IsDescribeParametersEligible(TestSupport.Ready("v1", "pipe-v1", protocolVersion: 1)));
        Assert.False(InstanceTargetResolver.IsDescribeParametersEligible(TestSupport.Ready("v2", "pipe-v2", protocolVersion: 2)));
        Assert.False(InstanceTargetResolver.IsDescribeParametersEligible(TestSupport.Ready("v3", "pipe-v3", protocolVersion: 3)));
        Assert.False(InstanceTargetResolver.IsDescribeParametersEligible(TestSupport.Ready("v4", "pipe-v4", protocolVersion: 4)));
        Assert.True(InstanceTargetResolver.IsDescribeParametersEligible(TestSupport.Ready("v5", "pipe-v5", protocolVersion: 5)));
        Assert.True(InstanceTargetResolver.IsDescribeParametersEligible(TestSupport.Ready("v6", "pipe-v6", protocolVersion: 6)));
        Assert.False(InstanceTargetResolver.IsDescribeParametersEligible(TestSupport.Ready("v7", "pipe-v7", protocolVersion: 7)));
    }

    [Fact]
    public void Inherited_capability_eligibility_remains_explicit()
    {
        Assert.True(InstanceTargetResolver.IsGetContextEligible(TestSupport.Ready("v2", "pipe-v2", protocolVersion: 2)));
        Assert.True(InstanceTargetResolver.IsGetContextEligible(TestSupport.Ready("v3", "pipe-v3", protocolVersion: 3)));
        Assert.True(InstanceTargetResolver.IsGetContextEligible(TestSupport.Ready("v4", "pipe-v4", protocolVersion: 4)));
        Assert.True(InstanceTargetResolver.IsGetContextEligible(TestSupport.Ready("v5", "pipe-v5", protocolVersion: 5)));
        Assert.False(InstanceTargetResolver.IsGetContextEligible(TestSupport.Ready("v1", "pipe-v1", protocolVersion: 1)));
        Assert.True(InstanceTargetResolver.IsGetContextEligible(TestSupport.Ready("v6", "pipe-v6", protocolVersion: 6)));
        Assert.False(InstanceTargetResolver.IsGetContextEligible(TestSupport.Ready("v7", "pipe-v7", protocolVersion: 7)));

        Assert.False(InstanceTargetResolver.IsQueryElementsEligible(TestSupport.Ready("v2", "pipe-v2", protocolVersion: 2)));
        Assert.True(InstanceTargetResolver.IsQueryElementsEligible(TestSupport.Ready("v3", "pipe-v3", protocolVersion: 3)));
        Assert.True(InstanceTargetResolver.IsQueryElementsEligible(TestSupport.Ready("v4", "pipe-v4", protocolVersion: 4)));
        Assert.True(InstanceTargetResolver.IsQueryElementsEligible(TestSupport.Ready("v5", "pipe-v5", protocolVersion: 5)));
        Assert.True(InstanceTargetResolver.IsQueryElementsEligible(TestSupport.Ready("v6", "pipe-v6", protocolVersion: 6)));
        Assert.False(InstanceTargetResolver.IsQueryElementsEligible(TestSupport.Ready("v7", "pipe-v7", protocolVersion: 7)));

        Assert.False(InstanceTargetResolver.IsGetElementsEligible(TestSupport.Ready("v3", "pipe-v3", protocolVersion: 3)));
        Assert.True(InstanceTargetResolver.IsGetElementsEligible(TestSupport.Ready("v4", "pipe-v4", protocolVersion: 4)));
        Assert.True(InstanceTargetResolver.IsGetElementsEligible(TestSupport.Ready("v5", "pipe-v5", protocolVersion: 5)));
        Assert.True(InstanceTargetResolver.IsGetElementsEligible(TestSupport.Ready("v6", "pipe-v6", protocolVersion: 6)));
        Assert.False(InstanceTargetResolver.IsGetElementsEligible(TestSupport.Ready("v7", "pipe-v7", protocolVersion: 7)));
    }

    [Fact]
    public async Task Zero_describe_capable_instances_returns_no_revit_instance()
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("v4", "pipe-v4", protocolVersion: 4));
        var factory = UnusedFactory();
        var outcome = await CreateService(discovery, factory).ExecuteAsync(null, TestSupport.CreateDescribeParametersRequest(), CancellationToken.None);

        Assert.Equal(McpToolErrorCodes.NoRevitInstance, outcome.ErrorCode);
        Assert.Empty(factory.RequestedPipes);
    }

    [Fact]
    public async Task Exactly_one_describe_capable_instance_is_auto_selected()
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("v4", "pipe-v4", protocolVersion: 4));
        discovery.Instances.Add(TestSupport.Ready("only", "pipe-only", protocolVersion: 5));
        var factory = SuccessfulFactory("only", "pipe-only");
        var outcome = await CreateService(discovery, factory).ExecuteAsync(null, TestSupport.CreateDescribeParametersRequest(), CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        Assert.Equal("only", outcome.Result!.Context.InstanceId);
        Assert.Equal(new[] { "pipe-only" }, factory.RequestedPipes);
    }

    [Fact]
    public async Task Multiple_describe_capable_instances_require_instance_id()
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("beta", "pipe-beta", protocolVersion: 5));
        discovery.Instances.Add(TestSupport.Ready("alpha", "pipe-alpha", protocolVersion: 5));
        var factory = UnusedFactory();
        var outcome = await CreateService(discovery, factory).ExecuteAsync(null, TestSupport.CreateDescribeParametersRequest(), CancellationToken.None);

        Assert.Equal(McpToolErrorCodes.InstanceRequired, outcome.ErrorCode);
        Assert.Equal(new[] { "alpha", "beta" }, outcome.Candidates!.Select(candidate => candidate.InstanceId));
        Assert.Equal(
            new[] { nameof(InstanceCandidate.InstanceId), nameof(InstanceCandidate.RevitBuild), nameof(InstanceCandidate.RevitVersion) },
            typeof(InstanceCandidate).GetProperties().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray());
        Assert.Empty(factory.RequestedPipes);
    }

    [Fact]
    public void Ambiguity_candidates_are_sorted_by_opaque_instance_id()
    {
        var discovered = new[]
        {
            TestSupport.Ready("m", "pipe-m", protocolVersion: 5),
            TestSupport.Ready("a", "pipe-a", protocolVersion: 5),
            TestSupport.Ready("z", "pipe-z", protocolVersion: 5)
        };

        var resolution = InstanceTargetResolver.ResolveForDescribeParameters(discovered, instanceId: null);

        Assert.Equal(McpToolErrorCodes.InstanceRequired, resolution.ErrorCode);
        Assert.Equal(new[] { "a", "m", "z" }, resolution.Candidates!.Select(candidate => candidate.InstanceId));
        Assert.All(resolution.Candidates!, candidate =>
        {
            Assert.False(string.IsNullOrWhiteSpace(candidate.RevitVersion));
            Assert.False(string.IsNullOrWhiteSpace(candidate.RevitBuild));
        });
    }

    [Fact]
    public async Task Explicit_unknown_id_returns_not_found()
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("alpha", "pipe-alpha", protocolVersion: 5));
        var factory = UnusedFactory();
        var outcome = await CreateService(discovery, factory).ExecuteAsync("missing", TestSupport.CreateDescribeParametersRequest(), CancellationToken.None);

        Assert.Equal(McpToolErrorCodes.InstanceNotFound, outcome.ErrorCode);
        Assert.Empty(factory.RequestedPipes);
    }

    [Fact]
    public async Task Explicit_v4_id_returns_unavailable()
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("v4", "pipe-v4", protocolVersion: 4));
        discovery.Instances.Add(TestSupport.Ready("v5", "pipe-v5", protocolVersion: 5));
        var factory = UnusedFactory();
        var outcome = await CreateService(discovery, factory).ExecuteAsync("v4", TestSupport.CreateDescribeParametersRequest(), CancellationToken.None);

        Assert.Equal(McpToolErrorCodes.InstanceUnavailable, outcome.ErrorCode);
        Assert.Empty(factory.RequestedPipes);
    }

    [Fact]
    public async Task Explicit_v5_id_is_selected()
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("v4", "pipe-v4", protocolVersion: 4));
        discovery.Instances.Add(TestSupport.Ready("v5", "pipe-v5", protocolVersion: 5));
        var factory = SuccessfulFactory("v5", "pipe-v5");
        var outcome = await CreateService(discovery, factory).ExecuteAsync("v5", TestSupport.CreateDescribeParametersRequest(), CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        Assert.Equal(new[] { "pipe-v5" }, factory.RequestedPipes);
    }

    [Fact]
    public async Task Explicit_v7_id_returns_unavailable()
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("v7", "pipe-v7", protocolVersion: 7));
        var factory = UnusedFactory();
        var outcome = await CreateService(discovery, factory).ExecuteAsync("v7", TestSupport.CreateDescribeParametersRequest(), CancellationToken.None);

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
        discovery.Instances.Add(TestSupport.Ready("only", "pipe-only", protocolVersion: 5));
        var factory = UnusedFactory();
        var outcome = await CreateService(discovery, factory).ExecuteAsync(instanceId, TestSupport.CreateDescribeParametersRequest(), CancellationToken.None);

        Assert.Equal(McpToolErrorCodes.InstanceNotFound, outcome.ErrorCode);
        Assert.Empty(factory.RequestedPipes);
    }

    [Fact]
    public async Task Each_invocation_performs_fresh_discovery()
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("only", "pipe-only", protocolVersion: 5));
        var factory = SuccessfulFactory("only", "pipe-only");
        var service = CreateService(discovery, factory);

        await service.ExecuteAsync(null, TestSupport.CreateDescribeParametersRequest(), CancellationToken.None);
        await service.ExecuteAsync(null, TestSupport.CreateDescribeParametersRequest(), CancellationToken.None);

        Assert.Equal(2, discovery.CallCount);
        Assert.Equal(2, factory.Clients.Count);
        Assert.All(factory.Clients, client => Assert.True(client.Disposed));
    }

    private static DescribeParametersApplicationService CreateService(
        FakeDiscovery discovery,
        RecordingBridgeClientFactory factory)
    {
        return new DescribeParametersApplicationService(discovery, factory, new ServerTimeouts
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
                    Handshake = (request, _) => Task.FromResult(TestSupport.CreateHandshake(registration, selectedProtocolVersion: 5)),
                    DescribeParameters = (_, _, _) => Task.FromResult(
                        TestSupport.CreateDescribeParametersResult(
                            instanceId,
                            "doc-1",
                            [TestSupport.CreateDescribeOkElement("ref-1")],
                            matchedCount: 0,
                            truncated: false))
                };
                return Task.FromResult(client);
            }
        };
    }
}
