using RevitMCP.Contracts;
using RevitMCP.Server;
using Xunit;

namespace RevitMCP.Server.Tests;

public sealed class GetContextRoutingTests
{
    [Fact]
    public async Task Zero_eligible_instances_returns_no_revit_instance()
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.WithState(DiscoveryState.Unavailable, "a", "pipe-a"));
        var factory = UnusedFactory();
        var outcome = await CreateService(discovery, factory).ExecuteAsync(null, CancellationToken.None);

        Assert.False(outcome.IsSuccess);
        Assert.Equal(McpToolErrorCodes.NoRevitInstance, outcome.ErrorCode);
        Assert.Empty(factory.RequestedPipes);
    }

    [Fact]
    public async Task Exactly_one_eligible_instance_is_auto_selected()
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("only", "pipe-only"));
        var factory = SuccessfulFactory("only", "pipe-only");
        var outcome = await CreateService(discovery, factory).ExecuteAsync(null, CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        Assert.Equal("only", outcome.Result!.Instance.InstanceId);
        Assert.Equal(new[] { "pipe-only" }, factory.RequestedPipes);
    }

    [Fact]
    public async Task Multiple_eligible_instances_require_instance_id()
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("beta", "pipe-beta"));
        discovery.Instances.Add(TestSupport.Ready("alpha", "pipe-alpha"));
        var factory = UnusedFactory();
        var outcome = await CreateService(discovery, factory).ExecuteAsync(null, CancellationToken.None);

        Assert.False(outcome.IsSuccess);
        Assert.Equal(McpToolErrorCodes.InstanceRequired, outcome.ErrorCode);
        Assert.Equal(new[] { "alpha", "beta" }, outcome.Candidates!.Select(candidate => candidate.InstanceId));
        Assert.All(outcome.Candidates!, candidate =>
        {
            Assert.Equal("2026", candidate.RevitVersion);
            Assert.Equal("26.5.0.55", candidate.RevitBuild);
        });
        Assert.Empty(factory.RequestedPipes);
        Assert.Empty(factory.Clients);
    }

    [Fact]
    public void Ambiguity_candidates_are_sorted_by_opaque_instance_id()
    {
        var discovered = new[]
        {
            TestSupport.Ready("m", "pipe-m"),
            TestSupport.Ready("a", "pipe-a"),
            TestSupport.Ready("z", "pipe-z")
        };

        var resolution = InstanceTargetResolver.Resolve(discovered, instanceId: null);

        Assert.Equal(McpToolErrorCodes.InstanceRequired, resolution.ErrorCode);
        Assert.Equal(new[] { "a", "m", "z" }, resolution.Candidates!.Select(candidate => candidate.InstanceId));
    }

    [Fact]
    public void Candidate_objects_expose_only_identity_and_revit_version_fields()
    {
        var names = typeof(InstanceCandidate).GetProperties().Select(property => property.Name).ToArray();
        Assert.Equal(new[] { nameof(InstanceCandidate.InstanceId), nameof(InstanceCandidate.RevitVersion), nameof(InstanceCandidate.RevitBuild) }, names);
    }

    [Fact]
    public async Task Explicit_eligible_id_selects_that_instance()
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("alpha", "pipe-alpha"));
        discovery.Instances.Add(TestSupport.Ready("beta", "pipe-beta"));
        var factory = SuccessfulFactory("beta", "pipe-beta");
        var outcome = await CreateService(discovery, factory).ExecuteAsync("beta", CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        Assert.Equal("beta", outcome.Result!.Instance.InstanceId);
        Assert.Equal(new[] { "pipe-beta" }, factory.RequestedPipes);
    }

    [Fact]
    public async Task Explicit_unknown_id_returns_not_found()
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("alpha", "pipe-alpha"));
        var factory = UnusedFactory();
        var outcome = await CreateService(discovery, factory).ExecuteAsync("missing", CancellationToken.None);

        Assert.Equal(McpToolErrorCodes.InstanceNotFound, outcome.ErrorCode);
        Assert.Empty(factory.RequestedPipes);
    }

    [Theory]
    [InlineData(DiscoveryState.Unavailable, null)]
    [InlineData(DiscoveryState.Incompatible, null)]
    [InlineData(DiscoveryState.Stale, null)]
    public async Task Explicit_non_ready_id_returns_unavailable(DiscoveryState state, int? protocol)
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.WithState(state, "target", "pipe-target", protocol));
        discovery.Instances.Add(TestSupport.Ready("other", "pipe-other"));
        var factory = UnusedFactory();
        var outcome = await CreateService(discovery, factory).ExecuteAsync("target", CancellationToken.None);

        Assert.Equal(McpToolErrorCodes.InstanceUnavailable, outcome.ErrorCode);
        Assert.Empty(factory.RequestedPipes);
    }

    [Fact]
    public async Task Explicit_ready_protocol_v1_returns_unavailable()
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("v1", "pipe-v1", protocolVersion: 1));
        discovery.Instances.Add(TestSupport.Ready("v2", "pipe-v2"));
        var factory = UnusedFactory();
        var outcome = await CreateService(discovery, factory).ExecuteAsync("v1", CancellationToken.None);

        Assert.Equal(McpToolErrorCodes.InstanceUnavailable, outcome.ErrorCode);
        Assert.Empty(factory.RequestedPipes);
    }

    [Fact]
    public void Discovery_order_does_not_change_unspecified_routing()
    {
        var first = new DiscoveredInstance[]
        {
            TestSupport.Ready("zeta", "pipe-zeta"),
            TestSupport.Ready("alpha", "pipe-alpha")
        };
        var second = first.Reverse().ToArray();

        var left = InstanceTargetResolver.Resolve(first, null);
        var right = InstanceTargetResolver.Resolve(second, null);

        Assert.Equal(McpToolErrorCodes.InstanceRequired, left.ErrorCode);
        Assert.Equal(left.ErrorCode, right.ErrorCode);
        Assert.Equal(
            left.Candidates!.Select(candidate => candidate.InstanceId),
            right.Candidates!.Select(candidate => candidate.InstanceId));
    }

    [Fact]
    public async Task Explicit_target_failure_does_not_select_an_alternate_instance()
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("alpha", "pipe-alpha"));
        discovery.Instances.Add(TestSupport.Ready("beta", "pipe-beta"));
        var factory = new RecordingBridgeClientFactory
        {
            Connect = (_, _, _) => throw new IOException("pipe closed")
        };

        var outcome = await CreateService(discovery, factory).ExecuteAsync("alpha", CancellationToken.None);

        Assert.Equal(McpToolErrorCodes.InstanceUnavailable, outcome.ErrorCode);
        Assert.Equal(new[] { "pipe-alpha" }, factory.RequestedPipes);
    }

    [Fact]
    public async Task Ambiguity_resolution_does_not_call_get_context_on_candidates()
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("alpha", "pipe-alpha"));
        discovery.Instances.Add(TestSupport.Ready("beta", "pipe-beta"));
        var factory = UnusedFactory();

        var outcome = await CreateService(discovery, factory).ExecuteAsync(null, CancellationToken.None);

        Assert.Equal(McpToolErrorCodes.InstanceRequired, outcome.ErrorCode);
        Assert.Empty(factory.RequestedPipes);
        Assert.Equal(1, discovery.CallCount);
    }

    [Fact]
    public async Task Each_invocation_performs_fresh_discovery()
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("only", "pipe-only"));
        var factory = SuccessfulFactory("only", "pipe-only");
        var service = CreateService(discovery, factory);

        await service.ExecuteAsync(null, CancellationToken.None);
        await service.ExecuteAsync(null, CancellationToken.None);

        Assert.Equal(2, discovery.CallCount);
    }

    private static GetContextApplicationService CreateService(
        FakeDiscovery discovery,
        RecordingBridgeClientFactory factory)
    {
        return new GetContextApplicationService(discovery, factory, new ServerTimeouts
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
                    Handshake = (request, _) => Task.FromResult(TestSupport.CreateHandshake(registration)),
                    GetContext = (_, _, _) => Task.FromResult(TestSupport.ZeroDocument(instanceId))
                };
                return Task.FromResult(client);
            }
        };
    }
}
