using RevitMCP.Contracts;

namespace RevitMCP.Bridge;

public static class RequestParameterUpdateReviewRequests
{
    public const int MaxIntentRefLength = 128;

    public static void Validate(RequestParameterUpdateReviewRequest? request)
    {
        if (request is null || !IsBoundedOpaqueRef(request.IntentRef))
        {
            throw new BridgeException(
                CapabilityErrorCodes.InvalidApprovalReviewRequest,
                "The approval review request is invalid.");
        }
    }

    public static bool IsBoundedOpaqueRef(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxIntentRefLength)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (char.IsWhiteSpace(character))
            {
                return false;
            }
        }

        return true;
    }
}
