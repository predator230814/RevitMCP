namespace BimIt.Revit.Assistant;

/// <summary>
/// Narrow trusted call for the existing MEP placement review. It cannot name another tool.
/// </summary>
internal interface IMepDevicePlacementReviewClient
{
    public const string ToolName = "revit_request_mep_device_placement_review";

    Task<AssistantToolCall> ReviewAsync(string instanceId, string intentRef, CancellationToken cancellationToken);
}
