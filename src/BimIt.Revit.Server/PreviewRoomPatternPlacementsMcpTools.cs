using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Server;

[McpServerToolType]
internal sealed class PreviewRoomPatternPlacementsMcpTools
{
    private readonly PreviewRoomPatternPlacementsApplicationService _application;

    public PreviewRoomPatternPlacementsMcpTools(PreviewRoomPatternPlacementsApplicationService application)
    {
        _application = application;
    }

    [McpServerTool(
        Name = PreviewRoomPatternPlacementsToolMetadata.Name,
        Title = PreviewRoomPatternPlacementsToolMetadata.Title,
        ReadOnly = false,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(PreviewRoomPatternPlacementsResult))]
    [Description(PreviewRoomPatternPlacementsToolMetadata.Description)]
    public async Task<CallToolResult> PreviewRoomPatternPlacementsAsync(
        McpServer server,
        [Description("Opaque active-document guard.")] string document_id,
        [Description("One to eighty pattern placements. Each names a room, a loaded family type, a role, boundary or interior placement, metres, and a quarter-turn.")] IReadOnlyList<PreviewRoomPatternPlacement> placements,
        [Description("Opaque RevitMCP instance identifier.")] string? instance_id = null,
        CancellationToken cancellationToken = default)
    {
        var request = new PreviewRoomPatternPlacementsRequest
        {
            DocumentId = document_id,
            Placements = placements,
        };
        var outcome = await _application.ExecuteAsync(instance_id, request, cancellationToken).ConfigureAwait(false);
        return outcome.IsSuccess
            ? McpCallResultFactory.Success(outcome.Result!, server.NegotiatedProtocolVersion)
            : McpCallResultFactory.Error(outcome.ErrorCode!, outcome.ErrorMessage!, outcome.Candidates);
    }
}
