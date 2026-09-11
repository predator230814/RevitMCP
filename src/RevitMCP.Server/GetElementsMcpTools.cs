using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RevitMCP.Contracts;

namespace RevitMCP.Server;

[McpServerToolType]
internal sealed class GetElementsMcpTools
{
    private readonly GetElementsApplicationService _application;

    public GetElementsMcpTools(GetElementsApplicationService application)
    {
        _application = application;
    }

    [McpServerTool(
        Name = GetElementsToolMetadata.Name,
        Title = GetElementsToolMetadata.Title,
        ReadOnly = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(GetElementsResult))]
    [Description(GetElementsToolMetadata.Description)]
    public async Task<CallToolResult> GetElementsAsync(
        McpServer server,
        [Description("Opaque active-document guard.")] string document_id,
        [Description("Opaque element references to inspect, in request order.")] IReadOnlyList<string> element_refs,
        [Description("Explicit field and named-parameter projection.")] GetElementsProjection projection,
        [Description("Opaque RevitMCP instance identifier.")] string? instance_id = null,
        CancellationToken cancellationToken = default)
    {
        if (document_id is null || element_refs is null || projection is null)
        {
            return McpCallResultFactory.Error(
                McpToolErrorCodes.InvalidRequest,
                ToolErrorMessages.InvalidRequest);
        }

        var request = new GetElementsRequest
        {
            DocumentId = document_id,
            ElementRefs = element_refs,
            Projection = projection
        };

        var outcome = await _application.ExecuteAsync(instance_id, request, cancellationToken).ConfigureAwait(false);
        return outcome.IsSuccess
            ? McpCallResultFactory.Success(outcome.Result!, ResolveProtocolVersion(server))
            : McpCallResultFactory.Error(outcome.ErrorCode!, outcome.ErrorMessage!, outcome.Candidates);
    }

    private static string? ResolveProtocolVersion(McpServer server)
    {
        return server.NegotiatedProtocolVersion;
    }
}
