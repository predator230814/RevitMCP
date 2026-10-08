using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RevitMCP.Bridge;
using RevitMCP.Contracts;

namespace RevitMCP.Server;

[McpServerToolType]
internal sealed class ApplyParameterUpdatesMcpTools
{
    private readonly ApplyParameterUpdatesApplicationService _application;

    public ApplyParameterUpdatesMcpTools(ApplyParameterUpdatesApplicationService application)
    {
        _application = application;
    }

    [McpServerTool(
        Name = ApplyParameterUpdatesToolMetadata.Name,
        Title = ApplyParameterUpdatesToolMetadata.Title,
        ReadOnly = false,
        Destructive = true,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ApplyParameterUpdatesResult))]
    [Description(ApplyParameterUpdatesToolMetadata.Description)]
    public async Task<CallToolResult> ApplyParameterUpdatesAsync(
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

        var request = new ApplyParameterUpdatesRequest
        {
            IntentRef = intent_ref
        };
        var outcome = await _application.ExecuteAsync(instance_id, request, cancellationToken).ConfigureAwait(false);
        return outcome.IsSuccess
            ? McpCallResultFactory.Success(outcome.Result!, server.NegotiatedProtocolVersion)
            : McpCallResultFactory.Error(outcome.ErrorCode!, outcome.ErrorMessage!);
    }
}
