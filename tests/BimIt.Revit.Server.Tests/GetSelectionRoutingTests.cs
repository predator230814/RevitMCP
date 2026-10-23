using BimIt.Revit.Bridge;
using BimIt.Revit.Contracts;
using BimIt.Revit.Server;
using Xunit;

namespace BimIt.Revit.Server.Tests;

public sealed class GetSelectionRoutingTests
{
    [Fact]
    public void Get_selection_eligibility_is_protocol_v13_only()
    {
        Assert.False(InstanceTargetResolver.IsGetSelectionEligible(TestSupport.Ready("v12", "pipe-v12", protocolVersion: 12)));
        Assert.True(InstanceTargetResolver.IsGetSelectionEligible(TestSupport.Ready("v13", "pipe-v13", protocolVersion: 13)));
        Assert.False(InstanceTargetResolver.IsGetSelectionEligible(TestSupport.Ready("v14", "pipe-v14", protocolVersion: 20)));
    }

    [Fact]
    public async Task Zero_eligible_instances_returns_no_revit_instance()
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("v12", "pipe-v12", protocolVersion: 12));
        var factory = UnusedFactory();

        var outcome = await CreateService(discovery, factory).ExecuteAsync(null, new GetSelectionRequest(), CancellationToken.None);

        Assert.Equal(McpToolErrorCodes.NoRevitInstance, outcome.ErrorCode);
        Assert.Empty(factory.RequestedPipes);
    }

    [Fact]
    public async Task One_v13_instance_is_auto_selected()
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("v12", "pipe-v12", protocolVersion: 12));
        discovery.Instances.Add(TestSupport.Ready("only", "pipe-only", protocolVersion: 13));
        var factory = SuccessfulFactory("only", "pipe-only");

        var outcome = await CreateService(discovery, factory).ExecuteAsync(null, new GetSelectionRequest(), CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        Assert.Equal(new[] { "pipe-only" }, factory.RequestedPipes);
    }

    [Fact]
    public async Task Multiple_v13_instances_require_an_instance_id()
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("b", "pipe-b", protocolVersion: 13));
        discovery.Instances.Add(TestSupport.Ready("a", "pipe-a", protocolVersion: 13));
        var factory = UnusedFactory();

        var outcome = await CreateService(discovery, factory).ExecuteAsync(null, new GetSelectionRequest(), CancellationToken.None);

        Assert.Equal(McpToolErrorCodes.InstanceRequired, outcome.ErrorCode);
        Assert.Equal(["a", "b"], outcome.Candidates!.Select(candidate => candidate.InstanceId).ToArray());
        Assert.Empty(factory.RequestedPipes);
    }

    [Fact]
    public async Task Explicit_eligible_v13_instance_is_selected()
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("other", "pipe-other", protocolVersion: 13));
        discovery.Instances.Add(TestSupport.Ready("chosen", "pipe-chosen", protocolVersion: 13));
        var factory = SuccessfulFactory("chosen", "pipe-chosen");

        var outcome = await CreateService(discovery, factory).ExecuteAsync("chosen", new GetSelectionRequest(), CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        Assert.Equal(new[] { "pipe-chosen" }, factory.RequestedPipes);
    }

    [Fact]
    public async Task Explicit_v12_instance_is_unavailable()
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("v12", "pipe-v12", protocolVersion: 12));
        var factory = UnusedFactory();

        var outcome = await CreateService(discovery, factory).ExecuteAsync("v12", new GetSelectionRequest(), CancellationToken.None);

        Assert.Equal(McpToolErrorCodes.InstanceUnavailable, outcome.ErrorCode);
        Assert.Empty(factory.RequestedPipes);
    }

    [Fact]
    public async Task Unknown_explicit_id_is_not_found()
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("only", "pipe-only", protocolVersion: 13));
        var factory = UnusedFactory();

        var outcome = await CreateService(discovery, factory).ExecuteAsync("missing", new GetSelectionRequest(), CancellationToken.None);

        Assert.Equal(McpToolErrorCodes.InstanceNotFound, outcome.ErrorCode);
        Assert.Empty(factory.RequestedPipes);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task Explicit_empty_or_whitespace_id_never_auto_selects(string instanceId)
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("only", "pipe-only", protocolVersion: 13));
        var factory = UnusedFactory();

        var outcome = await CreateService(discovery, factory).ExecuteAsync(instanceId, new GetSelectionRequest(), CancellationToken.None);

        Assert.Equal(McpToolErrorCodes.InstanceNotFound, outcome.ErrorCode);
        Assert.Empty(factory.RequestedPipes);
    }

    private static GetSelectionApplicationService CreateService(FakeDiscovery discovery, RecordingBridgeClientFactory factory)
    {
        return new GetSelectionApplicationService(discovery, factory, new ServerTimeouts());
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
                return Task.FromResult(new RecordingBridgeClient
                {
                    Handshake = (_, _) => Task.FromResult(TestSupport.CreateHandshake(registration, selectedProtocolVersion: 13)),
                    GetSelection = (_, _, _) => Task.FromResult(SelectionResult(instanceId))
                });
            }
        };
    }

    internal static GetSelectionResult SelectionResult(string instanceId, params string[] refs)
    {
        return new GetSelectionResult
        {
            Context = new GetSelectionContext
            {
                InstanceId = instanceId,
                DocumentId = "document-a"
            },
            SelectedCount = refs.Length,
            Truncated = false,
            ElementRefs = refs
        };
    }
}
