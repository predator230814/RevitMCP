namespace RevitMCP.Server;

internal static class RequestParameterUpdateReviewToolMetadata
{
    public const string Name = "revit_request_parameter_update_review";

    public const string Title = "Request Revit Parameter Update Review";

    public const string Description =
        "Ask the exact target Revit process to present an existing immutable parameter-update intent to the local user for review. Returns after the presentation request is started or fails. This does not approve the intent, wait for a human decision, consume approval, or modify the Revit model.";
}
