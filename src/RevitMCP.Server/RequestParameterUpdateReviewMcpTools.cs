using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RevitMCP.Bridge;
using RevitMCP.Contracts;

namespace RevitMCP.Server;

[McpServerToolType]
internal sealed class RequestParameterUpdateReviewMcpTools
{
    private readonly RequestParameterUpdateReviewApplicationService _application;

    public RequestParameterUpdateReviewMcpTools(RequestParameterUpdateReviewApplicationService application)
    {
        _application = application;
    }

    [McpServerTool(
        Name = RequestParameterUpdateReviewToolMetadata.Name,
        Title = RequestParameterUpdateReviewToolMetadata.Title,
        ReadOnly = false,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(RequestParameterUpdateReviewResult))]
    [Description(RequestParameterUpdateReviewToolMetadata.Description)]
    public async Task<CallToolResult> RequestParameterUpdateReviewAsync(
        McpServer server,
        [Description("Opaque RevitMCP instance identifier for the exact target process.")] string instance_id,
        [Description("Opaque intent reference returned by revit_preview_parameter_updates.")] string intent_ref,
        CancellationToken cancellationToken = default)
    {
        if (!RequestParameterUpdateReviewRequests.IsBoundedOpaqueRef(instance_id)
            || !RequestParameterUpdateReviewRequests.IsBoundedOpaqueRef(intent_ref))
        {
            return McpCallResultFactory.Error(
                McpToolErrorCodes.InvalidRequest,
                ToolErrorMessages.InvalidRequest);
        }

        var request = new RequestParameterUpdateReviewRequest
        {
            IntentRef = intent_ref
        };
        var outcome = await _application.ExecuteAsync(instance_id, request, cancellationToken).ConfigureAwait(false);
        return outcome.IsSuccess
            ? McpCallResultFactory.Success(outcome.Result!, server.NegotiatedProtocolVersion)
            : McpCallResultFactory.Error(outcome.ErrorCode!, outcome.ErrorMessage!);
    }
}
