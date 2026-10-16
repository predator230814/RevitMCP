using BimIt.Revit.Contracts;

namespace BimIt.Revit.Server;

internal sealed class RequestMepDevicePlacementReviewOutcome
{
    private RequestMepDevicePlacementReviewOutcome()
    {
    }

    public RequestMepDevicePlacementReviewResult? Result { get; private init; }

    public string? ErrorCode { get; private init; }

    public string? ErrorMessage { get; private init; }

    public bool IsSuccess => Result is not null;

    public static RequestMepDevicePlacementReviewOutcome Success(RequestMepDevicePlacementReviewResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new RequestMepDevicePlacementReviewOutcome { Result = result };
    }

    public static RequestMepDevicePlacementReviewOutcome Failure(string errorCode, string errorMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);
        return new RequestMepDevicePlacementReviewOutcome { ErrorCode = errorCode, ErrorMessage = errorMessage };
    }
}
