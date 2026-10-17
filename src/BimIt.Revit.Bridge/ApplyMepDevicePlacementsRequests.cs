using BimIt.Revit.Contracts;

namespace BimIt.Revit.Bridge;

public static class ApplyMepDevicePlacementsRequests
{
    public static void Validate(ApplyMepDevicePlacementsRequest? request)
    {
        if (request is null || !RequestParameterUpdateReviewRequests.IsBoundedOpaqueRef(request.IntentRef))
        {
            throw new BridgeException(
                CapabilityErrorCodes.InvalidApplyRequest,
                "The apply request is invalid.");
        }
    }
}
