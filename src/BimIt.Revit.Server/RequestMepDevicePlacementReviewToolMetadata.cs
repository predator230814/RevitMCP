namespace BimIt.Revit.Server;

internal static class RequestMepDevicePlacementReviewToolMetadata
{
    public const string Name = "revit_request_mep_device_placement_review";

    public const string Title = "Request MEP Device Placement Review";

    public const string Description =
        "Ask the exact target Revit process to present an existing immutable MEP device placement intent to the local user for review. Returns after the presentation request is started or fails. This does not approve the intent, wait for a human decision, consume approval, or modify the Revit model.";
}
