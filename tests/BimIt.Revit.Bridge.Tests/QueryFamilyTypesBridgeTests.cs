using BimIt.Revit.Bridge;
using BimIt.Revit.Bridge.Implementation;
using BimIt.Revit.Contracts;
using StreamJsonRpc;
using Xunit;

namespace BimIt.Revit.Bridge.Tests;

public sealed class QueryFamilyTypesBridgeTests
{
    [Theory]
    [InlineData(21)]
    [InlineData(23)]
    public async Task Versions_outside_v22_do_not_invoke_the_service(int version)
    {
        var service = new RecordingQuery();
        var adapter = Adapter(service, version);
        await adapter.HandshakeAsync(Handshake(version), CancellationToken.None);
        var rejected = await Assert.ThrowsAsync<LocalRpcException>(() =>
            adapter.QueryFamilyTypesAsync(new QueryFamilyTypesRequest { DocumentId = "doc" }, CancellationToken.None));
        Assert.Equal(BridgeErrorCodes.ProtocolIncompatible, Assert.IsType<BridgeError>(rejected.ErrorData).Code);
        Assert.Equal(0, service.Calls);
    }

    [Fact]
    public async Task Version_22_invokes_the_service_and_does_not_substitute_another_capability()
    {
        var service = new RecordingQuery();
        var adapter = Adapter(service, 22);
        await adapter.HandshakeAsync(Handshake(22), CancellationToken.None);
        var result = await adapter.QueryFamilyTypesAsync(new QueryFamilyTypesRequest { DocumentId = "doc" }, CancellationToken.None);
        Assert.Equal(1, service.Calls);
        Assert.True(result.Complete);
        Assert.Null(result.NextCursor);
    }

    private static StreamJsonRpcBridgeAdapter Adapter(IRevitQueryFamilyTypesService service, int version)
    {
        return new StreamJsonRpcBridgeAdapter(new SelectedVersionHandshake(version), capability: null, queryFamilyTypes: service);
    }

    private static BridgeHandshakeRequest Handshake(int version)
    {
        return new BridgeHandshakeRequest
        {
            ExpectedInstanceId = "any",
            SupportedProtocolVersions = [version],
            ClientName = "RevitMCP.Tests"
        };
    }

    private sealed class SelectedVersionHandshake : IRevitBridgeService
    {
        private readonly int _selected;

        public SelectedVersionHandshake(int selected)
        {
            _selected = selected;
        }

        public Task<BridgeHandshakeResult> HandshakeAsync(BridgeHandshakeRequest request, CancellationToken cancellationToken)
        {
            _ = request;
            _ = cancellationToken;
            return Task.FromResult(new BridgeHandshakeResult
            {
                InstanceId = "any",
                ProcessId = 1,
                ProcessStartTimeUtc = DateTimeOffset.UnixEpoch,
                WindowsSessionId = 1,
                RevitVersion = "2026",
                RevitBuild = "26.5.0.0",
                AddinVersion = "0.1.0",
                SupportedProtocolVersions = [_selected],
                SelectedProtocolVersion = _selected
            });
        }
    }

    private sealed class RecordingQuery : IRevitQueryFamilyTypesService
    {
        public int Calls { get; private set; }

        public Task<QueryFamilyTypesResult> QueryFamilyTypesAsync(QueryFamilyTypesRequest request, CancellationToken cancellationToken)
        {
            _ = request;
            _ = cancellationToken;
            Calls++;
            return Task.FromResult(new QueryFamilyTypesResult
            {
                Context = new QueryFamilyTypesContext { InstanceId = "any", DocumentId = "doc" },
                Summary = new QueryFamilyTypesSummary
                {
                    MatchedTypeCount = 0,
                    DistinctFamilyCount = 0,
                    PlacedTypeCount = 0,
                    PlacedInstanceCount = 0,
                    UsedFamilyCount = 0,
                    UnusedFamilyCount = 0
                },
                ReturnedCount = 0,
                Complete = true,
                NextCursor = null,
                Types = []
            });
        }
    }
}
