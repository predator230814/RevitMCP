using RevitMCP.Contracts;

namespace RevitMCP.Server;

internal sealed class RequestParameterUpdateReviewOutcome
{
    private RequestParameterUpdateReviewOutcome()
    {
    }

    public RequestParameterUpdateReviewResult? Result { get; private init; }

    public string? ErrorCode { get; private init; }

    public string? ErrorMessage { get; private init; }

    public bool IsSuccess => Result is not null;

    public static RequestParameterUpdateReviewOutcome Success(RequestParameterUpdateReviewResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new RequestParameterUpdateReviewOutcome { Result = result };
    }

    public static RequestParameterUpdateReviewOutcome Failure(string errorCode, string errorMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);
        return new RequestParameterUpdateReviewOutcome
        {
            ErrorCode = errorCode,
            ErrorMessage = errorMessage
        };
    }
}
