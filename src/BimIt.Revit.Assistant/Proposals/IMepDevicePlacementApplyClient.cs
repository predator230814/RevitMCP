namespace BimIt.Revit.Assistant;

/// <summary>
/// Narrow trusted call for the existing MEP placement apply. It cannot name another tool.
/// </summary>
internal interface IMepDevicePlacementApplyClient
{
    public const string ToolName = "revit_apply_mep_device_placements";

    Task<AssistantToolCall> ApplyAsync(string instanceId, string intentRef, CancellationToken cancellationToken);
}
