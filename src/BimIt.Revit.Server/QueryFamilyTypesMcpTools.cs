using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Server;

[McpServerToolType]
internal sealed class QueryFamilyTypesMcpTools
{
    private readonly QueryFamilyTypesApplicationService _application;

    public QueryFamilyTypesMcpTools(QueryFamilyTypesApplicationService application)
    {
        _application = application;
    }

    [McpServerTool(
        Name = QueryFamilyTypesToolMetadata.Name,
        Title = QueryFamilyTypesToolMetadata.Title,
        ReadOnly = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(QueryFamilyTypesResult))]
    [Description(QueryFamilyTypesToolMetadata.Description)]
    public async Task<CallToolResult> QueryFamilyTypesAsync(
        McpServer server,
        [Description("Opaque active-document guard from revit_get_context. Required.")] string? document_id,
        [Description("Optional stable BuiltInCategory tokens such as OST_ElectricalFixtures. One to twenty distinct values. Omit to include every category.")] string[]? category_tokens = null,
        [Description("Optional case-insensitive family-name substring, 1 to 256 characters. Not a wildcard.")] string? family_name_contains = null,
        [Description("Optional case-insensitive type-name substring, 1 to 256 characters. Not a wildcard.")] string? type_name_contains = null,
        [Description("all, placed, or unused. The default is all.")] string? usage = null,
        [Description("Maximum rows in this page, from 1 to 200. The default is 100. A page may be shorter.")] int? page_size = null,
        [Description("Opaque next_cursor from the previous page. Omit on the first page.")] string? cursor = null,
        [Description("Opaque RevitMCP instance identifier. Omit to auto-select the single eligible instance.")] string? instance_id = null,
        CancellationToken cancellationToken = default)
    {
        var request = new QueryFamilyTypesRequest
        {
            DocumentId = document_id,
            CategoryTokens = category_tokens,
            FamilyNameContains = family_name_contains,
            TypeNameContains = type_name_contains,
            Usage = usage,
            PageSize = page_size,
            Cursor = cursor
        };
        var outcome = await _application.ExecuteAsync(instance_id, request, cancellationToken).ConfigureAwait(false);
        return outcome.IsSuccess
            ? McpCallResultFactory.Success(outcome.Result!, server.NegotiatedProtocolVersion)
            : McpCallResultFactory.Error(outcome.ErrorCode!, outcome.ErrorMessage!, outcome.Candidates);
    }
}
