using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RevitMCP.Contracts;

namespace RevitMCP.Server;

[McpServerToolType]
internal sealed class QueryElementsMcpTools
{
    private readonly QueryElementsApplicationService _application;

    public QueryElementsMcpTools(QueryElementsApplicationService application)
    {
        _application = application;
    }

    [McpServerTool(
        Name = QueryElementsToolMetadata.Name,
        Title = QueryElementsToolMetadata.Title,
        ReadOnly = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(QueryElementsResult))]
    [Description(QueryElementsToolMetadata.Description)]
    public async Task<CallToolResult> QueryElementsAsync(
        McpServer server,
        [Description("Query scope: document or active_view.")] string scope,
        [Description("Bounded element filters. At least one filter is required.")] QueryElementFilters filters,
        [Description("Opaque RevitMCP instance identifier.")] string? instance_id = null,
        [Description("Opaque active-document guard.")] string? document_id = null,
        [Description("Maximum number of element references to return.")] int limit = QueryElementsToolMetadata.DefaultLimit,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseScope(scope, out var parsedScope))
        {
            return McpCallResultFactory.Error(
                CapabilityErrorCodes.InvalidQuery,
                "The query request is invalid.");
        }

        if (filters is null)
        {
            return McpCallResultFactory.Error(
                CapabilityErrorCodes.InvalidQuery,
                "The query request is invalid.");
        }

        var request = new QueryElementsRequest
        {
            DocumentId = document_id,
            Scope = parsedScope,
            Filters = filters,
            Limit = limit
        };

        var outcome = await _application.ExecuteAsync(instance_id, request, cancellationToken).ConfigureAwait(false);
        return outcome.IsSuccess
            ? McpCallResultFactory.Success(outcome.Result!, ResolveProtocolVersion(server))
            : McpCallResultFactory.Error(outcome.ErrorCode!, outcome.ErrorMessage!, outcome.Candidates);
    }

    private static bool TryParseScope(string? scope, out QueryScope parsed)
    {
        if (string.Equals(scope, "document", StringComparison.Ordinal))
        {
            parsed = QueryScope.Document;
            return true;
        }

        if (string.Equals(scope, "active_view", StringComparison.Ordinal))
        {
            parsed = QueryScope.ActiveView;
            return true;
        }

        parsed = default;
        return false;
    }

    private static string? ResolveProtocolVersion(McpServer server)
    {
        return server.NegotiatedProtocolVersion;
    }
}
