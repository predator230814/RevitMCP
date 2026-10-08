using RevitMCP.Contracts;

namespace RevitMCP.Bridge;

public static class ApplyParameterUpdatesRequests
{
    public const int MaxIntentRefLength = 128;

    public static void Validate(ApplyParameterUpdatesRequest? request)
    {
        if (request is null || !RequestParameterUpdateReviewRequests.IsBoundedOpaqueRef(request.IntentRef))
        {
            throw new BridgeException(
                CapabilityErrorCodes.InvalidApplyRequest,
                "The apply request is invalid.");
        }
    }
}
