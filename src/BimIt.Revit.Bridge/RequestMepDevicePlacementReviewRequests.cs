using BimIt.Revit.Contracts;

namespace BimIt.Revit.Bridge;

public static class RequestMepDevicePlacementReviewRequests
{
    public static void Validate(RequestMepDevicePlacementReviewRequest? request)
    {
        if (request is null || !RequestParameterUpdateReviewRequests.IsBoundedOpaqueRef(request.IntentRef))
        {
            throw new BridgeException(
                CapabilityErrorCodes.InvalidApprovalReviewRequest,
                "The approval review request is invalid.");
        }
    }
}
