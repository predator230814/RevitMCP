using BimIt.Revit.Bridge.Implementation;
using BimIt.Revit.Contracts;
using StreamJsonRpc;
using Xunit;

namespace BimIt.Revit.Bridge.Tests;

public sealed class BridgeV12ProtocolTests
{
    [Fact]
    public async Task V12_keeps_document_id_and_v11_omits_it()
    {
        var capability = new FakeCapabilityService
        {
            Result = DocumentResult("doc-opaque")
        };

        var v12 = await Context(capability, 12);
        Assert.Equal("doc-opaque", v12.Document!.DocumentId);

        var v11 = await Context(capability, 11);
        Assert.Null(v11.Document!.DocumentId);
        Assert.Equal("Hospital-MEP", v11.Document.Title);
        var v11Json = System.Text.Json.JsonSerializer.Serialize(v11, ContractJson.Options);
        Assert.DoesNotContain("document_id", v11Json, StringComparison.Ordinal);
        Assert.DoesNotContain("path", v11Json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("username", v11Json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task No_document_stays_null_on_v11_and_v12()
    {
        var capability = new FakeCapabilityService();
        Assert.Null((await Context(capability, 11)).Document);
        Assert.Null((await Context(capability, 12)).Document);
    }

    [Fact]
    public async Task Unknown_v14_does_not_call_get_context()
    {
        var capability = new FakeCapabilityService();
        var adapter = Adapter(capability, new FakeQueryElementsService(), 20);
        await adapter.HandshakeAsync(Handshake(), CancellationToken.None);
        var rejected = await Assert.ThrowsAsync<LocalRpcException>(() =>
            adapter.GetContextAsync(new GetContextRequest(), CancellationToken.None));
        Assert.Equal(BridgeErrorCodes.ProtocolIncompatible, ErrorCode(rejected));
        Assert.Equal(0, capability.InvokeCount);
    }

    [Fact]
    public async Task V11_rejects_empty_filters_and_v12_accepts_active_view()
    {
        var query = new FakeQueryElementsService();
        var v11 = Adapter(new FakeCapabilityService(), query, 11);
        await v11.HandshakeAsync(Handshake(), CancellationToken.None);
        var rejected = await Assert.ThrowsAsync<LocalRpcException>(() =>
            v11.QueryElementsAsync(Query(QueryScope.ActiveView), CancellationToken.None));
        Assert.Equal(CapabilityErrorCodes.InvalidQuery, ErrorCode(rejected));
        Assert.Equal(0, query.InvokeCount);

        var v12 = Adapter(new FakeCapabilityService(), query, 12);
        await v12.HandshakeAsync(Handshake(), CancellationToken.None);
        var result = await v12.QueryElementsAsync(Query(QueryScope.ActiveView), CancellationToken.None);
        Assert.Equal(0, result.MatchedCount);
        Assert.Equal(1, query.InvokeCount);
        Assert.Equal(QueryScope.ActiveView, query.LastRequest!.Scope);
        Assert.False(query.LastRequest.Filters.HasAny());
    }

    private static async Task<GetContextResult> Context(FakeCapabilityService capability, int version)
    {
        var adapter = Adapter(capability, new FakeQueryElementsService(), version);
        await adapter.HandshakeAsync(Handshake(), CancellationToken.None);
        return await adapter.GetContextAsync(new GetContextRequest(), CancellationToken.None);
    }

    private static StreamJsonRpcBridgeAdapter Adapter(FakeCapabilityService capability, FakeQueryElementsService query, int version)
    {
        return new StreamJsonRpcBridgeAdapter(new SelectedHandshake(version), capability, query);
    }

    private static BridgeHandshakeRequest Handshake()
    {
        return new BridgeHandshakeRequest
        {
            ExpectedInstanceId = "instance",
            SupportedProtocolVersions = [12, 11],
            ClientName = "RevitMCP.Tests"
        };
    }

    private static QueryElementsRequest Query(QueryScope scope)
    {
        return new QueryElementsRequest
        {
            Scope = scope,
            Filters = new QueryElementFilters(),
            Limit = 25
        };
    }

    private static GetContextResult DocumentResult(string documentId)
    {
        var result = FakeCapabilityService.CreateProjectResult();
        return new GetContextResult
        {
            Instance = result.Instance,
            Document = new GetContextDocument
            {
                DocumentId = documentId,
                Title = result.Document!.Title,
                Kind = result.Document.Kind,
                IsWorkshared = result.Document.IsWorkshared,
                IsModelInCloud = result.Document.IsModelInCloud,
                IsReadOnly = result.Document.IsReadOnly,
                IsModified = result.Document.IsModified
            },
            ActiveView = result.ActiveView,
            Selection = result.Selection
        };
    }

    private static string ErrorCode(LocalRpcException exception)
    {
        return Assert.IsType<BridgeError>(exception.ErrorData).Code;
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
}
