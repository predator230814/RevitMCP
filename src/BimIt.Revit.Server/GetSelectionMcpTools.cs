using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Server;

[McpServerToolType]
internal sealed class GetSelectionMcpTools
{
    private readonly GetSelectionApplicationService _application;

    public GetSelectionMcpTools(GetSelectionApplicationService application)
    {
        _application = application;
    }

    [McpServerTool(
        Name = GetSelectionToolMetadata.Name,
        Title = GetSelectionToolMetadata.Title,
        ReadOnly = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(GetSelectionResult))]
    [Description(GetSelectionToolMetadata.Description)]
    public async Task<CallToolResult> GetSelectionAsync(
        McpServer server,
        [Description("Opaque RevitMCP instance identifier. Omit to auto-select the single eligible instance.")] string? instance_id = null,
        [Description("Opaque active-document guard. Omit to use the current active document.")] string? document_id = null,
        [Description("Maximum number of element references to return. Omit to let the capability apply its default.")] int? limit = null,
        CancellationToken cancellationToken = default)
    {
        var request = new GetSelectionRequest
        {
            DocumentId = document_id,
            Limit = limit
        };

        var outcome = await _application.ExecuteAsync(instance_id, request, cancellationToken).ConfigureAwait(false);
        return outcome.IsSuccess
            ? McpCallResultFactory.Success(outcome.Result!, server.NegotiatedProtocolVersion)
            : McpCallResultFactory.Error(outcome.ErrorCode!, outcome.ErrorMessage!, outcome.Candidates);
    }
}
