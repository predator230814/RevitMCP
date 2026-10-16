using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Server;

[McpServerToolType]
internal sealed class PreviewMepDevicePlacementsMcpTools
{
    private readonly PreviewMepDevicePlacementsApplicationService _application;

    public PreviewMepDevicePlacementsMcpTools(PreviewMepDevicePlacementsApplicationService application)
    {
        _application = application;
    }

    [McpServerTool(
        Name = PreviewMepDevicePlacementsToolMetadata.Name,
        Title = PreviewMepDevicePlacementsToolMetadata.Title,
        ReadOnly = false,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(PreviewMepDevicePlacementsResult))]
    [Description(PreviewMepDevicePlacementsToolMetadata.Description)]
    public async Task<CallToolResult> PreviewMepDevicePlacementsAsync(
        McpServer server,
        [Description("Opaque active-document guard.")] string document_id,
        [Description("Opaque existing MEP device used as the placement template.")] string template_element_ref,
        [Description("Forge type id of a Revit length unit.")] string length_unit_id,
        [Description("Signed offsets from the template, in request order.")] IReadOnlyList<PreviewMepDevicePlacement> placements,
        [Description("Opaque RevitMCP instance identifier.")] string? instance_id = null,
        CancellationToken cancellationToken = default)
    {
        if (document_id is null
            || template_element_ref is null
            || length_unit_id is null
            || placements is null
            || placements.Count is < 1 or > 20
            || placements.Any(placement => placement is null
                || !double.IsFinite(placement.OffsetX)
                || !double.IsFinite(placement.OffsetY)
                || !double.IsFinite(placement.VerticalOffset))
            || HasDuplicateTriples(placements))
        {
            return McpCallResultFactory.Error(
                McpToolErrorCodes.InvalidRequest,
                ToolErrorMessages.InvalidRequest);
        }

        var request = new PreviewMepDevicePlacementsRequest
        {
            DocumentId = document_id,
            TemplateElementRef = template_element_ref,
            LengthUnitId = length_unit_id,
            Placements = placements
        };
        var outcome = await _application.ExecuteAsync(instance_id, request, cancellationToken).ConfigureAwait(false);
        return outcome.IsSuccess
            ? McpCallResultFactory.Success(outcome.Result!, server.NegotiatedProtocolVersion)
            : McpCallResultFactory.Error(outcome.ErrorCode!, outcome.ErrorMessage!, outcome.Candidates);
    }

    private static bool HasDuplicateTriples(IReadOnlyList<PreviewMepDevicePlacement> placements)
    {
        var seen = new HashSet<(double X, double Y, double Vertical)>();
        foreach (var placement in placements)
        {
            if (!seen.Add((placement.OffsetX, placement.OffsetY, placement.VerticalOffset)))
            {
                return true;
            }
        }

        return false;
    }
}
