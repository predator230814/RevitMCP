using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using BimIt.Revit.Bridge;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Server;

[McpServerToolType]
internal sealed class ApplyMepDevicePlacementsMcpTools
{
    private readonly ApplyMepDevicePlacementsApplicationService _application;

    public ApplyMepDevicePlacementsMcpTools(ApplyMepDevicePlacementsApplicationService application)
    {
        _application = application;
    }

    [McpServerTool(
        Name = ApplyMepDevicePlacementsToolMetadata.Name,
        Title = ApplyMepDevicePlacementsToolMetadata.Title,
        ReadOnly = false,
        Destructive = true,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ApplyMepDevicePlacementsResult))]
    [Description(ApplyMepDevicePlacementsToolMetadata.Description)]
    public async Task<CallToolResult> ApplyMepDevicePlacementsAsync(
        McpServer server,
        [Description("Opaque RevitMCP instance identifier for the exact target process.")] string instance_id,
        [Description("Opaque intent reference returned by revit_preview_mep_device_placements.")] string intent_ref,
        CancellationToken cancellationToken = default)
    {
        if (!RequestParameterUpdateReviewRequests.IsBoundedOpaqueRef(instance_id)
            || !RequestParameterUpdateReviewRequests.IsBoundedOpaqueRef(intent_ref))
        {
            return McpCallResultFactory.Error(
                McpToolErrorCodes.InvalidRequest,
                ToolErrorMessages.InvalidRequest);
        }

        var request = new ApplyMepDevicePlacementsRequest { IntentRef = intent_ref };
        var outcome = await _application.ExecuteAsync(instance_id, request, cancellationToken).ConfigureAwait(false);
        return outcome.IsSuccess
            ? McpCallResultFactory.Success(outcome.Result!, server.NegotiatedProtocolVersion)
            : McpCallResultFactory.Error(outcome.ErrorCode!, outcome.ErrorMessage!);
    }
}
