using RevitMCP.Bridge;
using RevitMCP.Contracts;
using RevitMCP.Server;
using Xunit;

namespace RevitMCP.Server.Tests;

public sealed class RequestParameterUpdateReviewBridgeOrchestrationTests
{
    [Fact]
    public async Task Handshake_offers_the_full_supported_prefix_and_preserves_status()
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("v9", "pipe-v9", protocolVersion: 9));
        RecordingBridgeClient? client = null;
        var factory = new RecordingBridgeClientFactory
        {
            Connect = (pipe, _, _) =>
            {
                var registration = TestSupport.CreateRegistration("v9", pipe);
                client = new RecordingBridgeClient
                {
                    Handshake = (request, _) =>
                    {
                        Assert.Equal(BridgeProtocol.SupportedVersions, request.SupportedProtocolVersions);
                        return Task.FromResult(TestSupport.CreateHandshake(registration, selectedProtocolVersion: 9));
                    },
                    RequestParameterUpdateReview = (request, timeout, _) =>
                    {
                        Assert.Equal("intent-ref", request.IntentRef);
                        Assert.Equal(TimeSpan.FromMilliseconds(75), timeout);
                        return Task.FromResult(new RequestParameterUpdateReviewResult
                        {
                            Status = RequestParameterUpdateReviewStatus.AlreadyActive
                        });
                    }
                };
                return Task.FromResult(client);
            }
        };

        var outcome = await new RequestParameterUpdateReviewApplicationService(discovery, factory, new ServerTimeouts
        {
            BootstrapTimeout = TimeSpan.FromMilliseconds(50),
            CapabilityTimeout = TimeSpan.FromMilliseconds(75)
        }).ExecuteAsync("v9", new RequestParameterUpdateReviewRequest { IntentRef = "intent-ref" }, CancellationToken.None);

        Assert.Equal(RequestParameterUpdateReviewStatus.AlreadyActive, outcome.Result!.Status);
        Assert.Equal(1, client!.RequestParameterUpdateReviewCalls);
    }

    [Theory]
    [InlineData(RequestParameterUpdateReviewStatus.Started)]
    [InlineData(RequestParameterUpdateReviewStatus.AlreadyActive)]
    [InlineData(RequestParameterUpdateReviewStatus.Busy)]
    [InlineData(RequestParameterUpdateReviewStatus.Unavailable)]
    [InlineData(RequestParameterUpdateReviewStatus.Terminal)]
    public async Task Bridge_status_is_preserved(RequestParameterUpdateReviewStatus status)
    {
        var outcome = await InvokeAsync(status, error: null);
        Assert.Equal(status, outcome.Result!.Status);
    }

    [Fact]
    public async Task Invalid_review_request_is_mapped_and_unknown_errors_become_unavailable()
    {
        var invalid = await InvokeAsync(RequestParameterUpdateReviewStatus.Started, new BridgeException(
            CapabilityErrorCodes.InvalidApprovalReviewRequest,
            "The approval review request is invalid."));
        Assert.Equal(CapabilityErrorCodes.InvalidApprovalReviewRequest, invalid.ErrorCode);

        var foreign = await InvokeAsync(RequestParameterUpdateReviewStatus.Started, new BridgeException(
            CapabilityErrorCodes.NoActiveDocument,
            "No active document."));
        Assert.Equal(McpToolErrorCodes.InstanceUnavailable, foreign.ErrorCode);
    }

    [Fact]
    public async Task Negotiated_v8_does_not_invoke_request_review()
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("v9", "pipe-v9", protocolVersion: 9));
        var factory = new RecordingBridgeClientFactory
        {
            Connect = (pipe, _, _) =>
            {
                var registration = TestSupport.CreateRegistration("v9", pipe);
                return Task.FromResult(new RecordingBridgeClient
                {
                    Handshake = (_, _) => Task.FromResult(TestSupport.CreateHandshake(registration, selectedProtocolVersion: 8)),
                    RequestParameterUpdateReview = (_, _, _) => throw new InvalidOperationException("Request review must not run for v8.")
                });
            }
        };

        var outcome = await new RequestParameterUpdateReviewApplicationService(discovery, factory, new ServerTimeouts())
            .ExecuteAsync("v9", new RequestParameterUpdateReviewRequest { IntentRef = "intent-ref" }, CancellationToken.None);

        Assert.Equal(McpToolErrorCodes.InstanceUnavailable, outcome.ErrorCode);
        Assert.Equal(0, factory.Clients[0].RequestParameterUpdateReviewCalls);
    }

    private static async Task<RequestParameterUpdateReviewOutcome> InvokeAsync(
        RequestParameterUpdateReviewStatus status,
        BridgeException? error)
    {
        var discovery = new FakeDiscovery();
        discovery.Instances.Add(TestSupport.Ready("v9", "pipe-v9", protocolVersion: 9));
        var factory = new RecordingBridgeClientFactory
        {
            Connect = (pipe, _, _) =>
            {
                var registration = TestSupport.CreateRegistration("v9", pipe);
                return Task.FromResult(new RecordingBridgeClient
                {
                    Handshake = (_, _) => Task.FromResult(TestSupport.CreateHandshake(registration, selectedProtocolVersion: 9)),
                    RequestParameterUpdateReview = (_, _, _) => error is null
                        ? Task.FromResult(new RequestParameterUpdateReviewResult { Status = status })
                        : Task.FromException<RequestParameterUpdateReviewResult>(error)
                });
            }
        };
        return await new RequestParameterUpdateReviewApplicationService(discovery, factory, new ServerTimeouts())
            .ExecuteAsync("v9", new RequestParameterUpdateReviewRequest { IntentRef = "intent-ref" }, CancellationToken.None);
    }
}
