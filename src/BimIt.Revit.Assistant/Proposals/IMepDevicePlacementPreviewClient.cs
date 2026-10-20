using BimIt.Revit.Contracts;

namespace BimIt.Revit.Assistant;

/// <summary>
/// Narrow trusted call for one existing MCP preview. It is not a model tool and cannot name another tool.
/// </summary>
internal interface IMepDevicePlacementPreviewClient
{
    public const string ToolName = "revit_preview_mep_device_placements";

    Task<AssistantToolCall> PreviewAsync(
        PreviewMepDevicePlacementsRequest request,
        string instanceId,
        CancellationToken cancellationToken);
}
