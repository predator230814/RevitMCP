using RevitMCP.Contracts;
using RevitMCP.Server;
using Xunit;

namespace RevitMCP.Server.Tests;

public sealed class RequestParameterUpdateReviewRoutingTests
{
    [Fact]
    public void Review_eligibility_is_protocol_v9_only()
    {
        for (var version = 1; version <= 8; version++)
        {
            Assert.False(InstanceTargetResolver.IsRequestParameterUpdateReviewEligible(
                TestSupport.Ready("v" + version, "pipe-v" + version, protocolVersion: version)));
        }

        Assert.True(InstanceTargetResolver.IsRequestParameterUpdateReviewEligible(TestSupport.Ready("v9", "pipe-v9", protocolVersion: 9)));
        Assert.True(InstanceTargetResolver.IsRequestParameterUpdateReviewEligible(TestSupport.Ready("v10", "pipe-v10", protocolVersion: 10)));
        Assert.True(InstanceTargetResolver.IsRequestParameterUpdateReviewEligible(TestSupport.Ready("v11", "pipe-v11", protocolVersion: 11)));
        Assert.False(InstanceTargetResolver.IsRequestParameterUpdateReviewEligible(TestSupport.Ready("v12", "pipe-v12", protocolVersion: 12)));
    }

    [Fact]
    public async Task Exact_v9_instance_is_selected()
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("v8", "pipe-v8", protocolVersion: 8));
        discovery.Instances.Add(TestSupport.Ready("v9", "pipe-v9", protocolVersion: 9));
        var factory = SuccessfulFactory("v9", "pipe-v9");
        var outcome = await CreateService(discovery, factory).ExecuteAsync("v9", Request(), CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        Assert.Equal(RequestParameterUpdateReviewStatus.Started, outcome.Result!.Status);
        Assert.Equal(new[] { "pipe-v9" }, factory.RequestedPipes);
    }

    [Fact]
    public async Task V8_instance_is_unavailable_and_does_not_fall_back()
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("v8", "pipe-v8", protocolVersion: 8));
        discovery.Instances.Add(TestSupport.Ready("other", "pipe-other", protocolVersion: 9));
        var factory = UnusedFactory();
        var outcome = await CreateService(discovery, factory).ExecuteAsync("v8", Request(), CancellationToken.None);

        Assert.Equal(McpToolErrorCodes.InstanceUnavailable, outcome.ErrorCode);
        Assert.Empty(factory.RequestedPipes);
    }

    [Fact]
    public async Task Missing_exact_instance_does_not_fall_back()
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("v9", "pipe-v9", protocolVersion: 9));
        var factory = UnusedFactory();
        var outcome = await CreateService(discovery, factory).ExecuteAsync("missing", Request(), CancellationToken.None);

        Assert.Equal(McpToolErrorCodes.InstanceNotFound, outcome.ErrorCode);
        Assert.NotEqual(McpToolErrorCodes.InstanceRequired, outcome.ErrorCode);
        Assert.Empty(factory.RequestedPipes);
    }

    [Fact]
    public async Task Omitted_instance_id_does_not_auto_select()
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("only", "pipe-only", protocolVersion: 9));
        var factory = UnusedFactory();
        var outcome = await CreateService(discovery, factory).ExecuteAsync(null!, Request(), CancellationToken.None);

        Assert.Equal(McpToolErrorCodes.InstanceNotFound, outcome.ErrorCode);
        Assert.Empty(factory.RequestedPipes);
    }

    private static RequestParameterUpdateReviewApplicationService CreateService(
        FakeDiscovery discovery,
        RecordingBridgeClientFactory factory)
    {
        return new RequestParameterUpdateReviewApplicationService(discovery, factory, new ServerTimeouts());
    }

    private static RequestParameterUpdateReviewRequest Request()
    {
        return new RequestParameterUpdateReviewRequest { IntentRef = "intent-ref" };
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
                    Handshake = (_, _) => Task.FromResult(TestSupport.CreateHandshake(registration, selectedProtocolVersion: 9)),
                    RequestParameterUpdateReview = (_, _, _) => Task.FromResult(new RequestParameterUpdateReviewResult
                    {
                        Status = RequestParameterUpdateReviewStatus.Started
                    })
                };
                return Task.FromResult(client);
            }
        };
    }
}
