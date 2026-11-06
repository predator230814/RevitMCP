using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Server;

[McpServerToolType]
internal sealed class QueryParameterBindingsMcpTools
{
    private readonly QueryParameterBindingsApplicationService _application;

    public QueryParameterBindingsMcpTools(QueryParameterBindingsApplicationService application)
    {
        _application = application;
    }

    [McpServerTool(
        Name = QueryParameterBindingsToolMetadata.Name,
        Title = QueryParameterBindingsToolMetadata.Title,
        ReadOnly = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(QueryParameterBindingsResult))]
    [Description(QueryParameterBindingsToolMetadata.Description)]
    public async Task<CallToolResult> QueryParameterBindingsAsync(
        McpServer server,
        [Description("Opaque active-document guard from revit_get_context. Required.")] string? document_id,
        [Description("Optional case-insensitive definition-name substring, 1 to 256 characters. Not a wildcard.")] string? name_contains = null,
        [Description("all, shared, local, or built_in. The default is all.")] string? identity_kind = null,
        [Description("all, instance, or type. The default is all.")] string? binding_kind = null,
        [Description("Optional stable BuiltInCategory tokens such as OST_ElectricalFixtures. One to twenty distinct values. A row matches when any of its bound categories has one of these tokens.")] string[]? category_tokens = null,
        [Description("Maximum rows in this page, from 1 to 100. The default is 50. A page may be shorter.")] int? page_size = null,
        [Description("Opaque next_cursor from the previous page. Pass it back unchanged with the same query. Omit on the first page.")] string? cursor = null,
        [Description("Opaque RevitMCP instance identifier. Omit to auto-select the single eligible instance.")] string? instance_id = null,
        CancellationToken cancellationToken = default)
    {
        var request = new QueryParameterBindingsRequest
        {
            DocumentId = document_id,
            NameContains = name_contains,
            IdentityKind = identity_kind,
            BindingKind = binding_kind,
            CategoryTokens = category_tokens,
            PageSize = page_size,
            Cursor = cursor
        };
        var outcome = await _application.ExecuteAsync(instance_id, request, cancellationToken).ConfigureAwait(false);
        return outcome.IsSuccess
            ? McpCallResultFactory.Success(outcome.Result!, server.NegotiatedProtocolVersion)
            : McpCallResultFactory.Error(outcome.ErrorCode!, outcome.ErrorMessage!, outcome.Candidates);
    }
}
