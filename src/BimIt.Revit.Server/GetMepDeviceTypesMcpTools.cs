using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Server;

[McpServerToolType]
internal sealed class GetMepDeviceTypesMcpTools
{
    private readonly GetMepDeviceTypesApplicationService _application;

    public GetMepDeviceTypesMcpTools(GetMepDeviceTypesApplicationService application)
    {
        _application = application;
    }

    [McpServerTool(
        Name = GetMepDeviceTypesToolMetadata.Name,
        Title = GetMepDeviceTypesToolMetadata.Title,
        ReadOnly = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(GetMepDeviceTypesResult))]
    [Description(GetMepDeviceTypesToolMetadata.Description)]
    public async Task<CallToolResult> GetMepDeviceTypesAsync(
        McpServer server,
        [Description("Opaque active-document guard. Required.")] string? document_id,
        [Description("Optional stable BuiltInCategory tokens such as OST_ElectricalFixtures. Omit to include every supported MEP device category.")] string[]? category_tokens = null,
        [Description("Optional case-insensitive family or type text search, at most 80 characters.")] string? search = null,
        [Description("Maximum types to return, from 1 to 80. The default is 40.")] int? limit = null,
        [Description("Opaque RevitMCP instance identifier. Omit to auto-select the single eligible instance.")] string? instance_id = null,
        CancellationToken cancellationToken = default)
    {
        var request = new GetMepDeviceTypesRequest
        {
            DocumentId = document_id,
            CategoryTokens = category_tokens,
            Search = search,
            Limit = limit,
        };
        var outcome = await _application.ExecuteAsync(instance_id, request, cancellationToken).ConfigureAwait(false);
        return outcome.IsSuccess
            ? McpCallResultFactory.Success(outcome.Result!, server.NegotiatedProtocolVersion)
            : McpCallResultFactory.Error(outcome.ErrorCode!, outcome.ErrorMessage!, outcome.Candidates);
    }
}
