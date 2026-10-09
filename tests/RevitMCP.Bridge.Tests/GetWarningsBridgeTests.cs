using RevitMCP.Bridge.Implementation;
using RevitMCP.Contracts;
using StreamJsonRpc;
using Xunit;

namespace RevitMCP.Bridge.Tests;

public sealed class GetWarningsBridgeTests
{
    [Fact]
    public void V11_is_advertised_only_with_the_complete_prefix()
    {
        var metadata = TestSupport.CreateMetadata();
        var services = Full();
        var warnings = new FakeGetWarningsService();
        var v11 = NamedPipeBridgeHost.WithAdvertisedProtocol(
            metadata,
            services.Capability,
            services.Query,
            services.Elements,
            services.Describe,
            services.Values,
            services.Topology,
            services.Preview,
            services.Review,
            services.Apply,
            warnings);
        var v10 = NamedPipeBridgeHost.WithAdvertisedProtocol(
            metadata,
            services.Capability,
            services.Query,
            services.Elements,
            services.Describe,
            services.Values,
            services.Topology,
            services.Preview,
            services.Review,
            services.Apply);

        Assert.Equal(BridgeProtocol.SupportedVersions, v11.SupportedProtocolVersions);
        Assert.Equal(11, v11.SupportedProtocolVersions.Max());
        Assert.Equal(BridgeProtocol.ApplyParameterUpdatesVersions, v10.SupportedProtocolVersions);
        Assert.DoesNotContain(11, v10.SupportedProtocolVersions);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(12)]
    public async Task Get_warnings_is_rejected_outside_v11(int version)
    {
        var service = new FakeGetWarningsService();
        var adapter = Adapter(service, version);
        await adapter.HandshakeAsync(Handshake(), CancellationToken.None);
        var rejected = await Assert.ThrowsAsync<LocalRpcException>(() =>
            adapter.GetWarningsAsync(Request(), CancellationToken.None));
        Assert.Equal(BridgeErrorCodes.ProtocolIncompatible, ErrorCode(rejected));
        Assert.Equal(0, service.InvokeCount);
    }

    [Fact]
    public async Task Get_warnings_is_allowed_on_v11_and_capability_errors_survive()
    {
        var service = new FakeGetWarningsService();
        var adapter = Adapter(service, 11);
        await adapter.HandshakeAsync(Handshake(), CancellationToken.None);
        var result = await adapter.GetWarningsAsync(Request(), CancellationToken.None);
        Assert.Equal(0, result.MatchedCount);
        Assert.Equal(1, service.InvokeCount);

        service.Error = new BridgeException(CapabilityErrorCodes.InvalidWarnings, "invalid");
        var failed = await Assert.ThrowsAsync<LocalRpcException>(() =>
            adapter.GetWarningsAsync(Request(), CancellationToken.None));
        Assert.Equal(CapabilityErrorCodes.InvalidWarnings, ErrorCode(failed));
    }

    private static StreamJsonRpcBridgeAdapter Adapter(FakeGetWarningsService service, int version)
    {
        return new StreamJsonRpcBridgeAdapter(new SelectedHandshake(version), capability: null, getWarnings: service);
    }

    private static BridgeHandshakeRequest Handshake()
    {
        return new BridgeHandshakeRequest
        {
            ExpectedInstanceId = "instance",
            SupportedProtocolVersions = [11],
            ClientName = "RevitMCP.Tests"
        };
    }

    private static GetWarningsRequest Request()
    {
        return new GetWarningsRequest { DocumentId = "document" };
    }

    private static string ErrorCode(LocalRpcException exception)
    {
        return Assert.IsType<BridgeError>(exception.ErrorData).Code;
    }

    private static FullServices Full()
    {
        return new FullServices(
            new FakeCapabilityService(),
            new FakeQueryElementsService(),
            new FakeGetElementsService(),
            new FakeDescribeParametersService(),
            new FakeGetParameterValuesService(),
            new FakeGetMepTopologyService(),
            new FakePreviewParameterUpdatesService(),
            new FakeRequestParameterUpdateReviewService(),
            new FakeApplyParameterUpdatesService());
    }

    private sealed class SelectedHandshake : IRevitBridgeService
    {
        private readonly int _selected;

        public SelectedHandshake(int selected) => _selected = selected;

        public Task<BridgeHandshakeResult> HandshakeAsync(BridgeHandshakeRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new BridgeHandshakeResult
            {
                InstanceId = request.ExpectedInstanceId,
                ProcessId = 1,
                ProcessStartTimeUtc = DateTimeOffset.UnixEpoch,
                WindowsSessionId = 1,
                RevitVersion = "2026",
                RevitBuild = "test",
                AddinVersion = "test",
                SelectedProtocolVersion = _selected,
                SupportedProtocolVersions = [_selected]
            });
        }
    }

    private readonly record struct FullServices(
        IRevitCapabilityService Capability,
        IRevitQueryElementsService Query,
        IRevitGetElementsService Elements,
        IRevitDescribeParametersService Describe,
        IRevitGetParameterValuesService Values,
        IRevitGetMepTopologyService Topology,
        IRevitPreviewParameterUpdatesService Preview,
        IRevitRequestParameterUpdateReviewService Review,
        IRevitApplyParameterUpdatesService Apply);
}

internal sealed class FakeGetWarningsService : IRevitGetWarningsService
{
    public int InvokeCount { get; private set; }

    public Exception? Error { get; set; }

    public Task<GetWarningsResult> GetWarningsAsync(GetWarningsRequest request, CancellationToken cancellationToken)
    {
        InvokeCount++;
        if (Error is not null)
        {
            throw Error;
        }

        return Task.FromResult(new GetWarningsResult
        {
            Context = new GetWarningsContext { InstanceId = "instance", DocumentId = request.DocumentId },
            MatchedCount = 0,
            CountsBySeverity = new GetWarningsCounts
            {
                Warning = 0,
                Error = 0,
                DocumentCorruption = 0,
                Other = 0
            },
            Definitions = [],
            Warnings = [],
            Truncated = false,
            TruncationReasons = []
        });
    }
}
