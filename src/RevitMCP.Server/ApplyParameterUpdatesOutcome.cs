using RevitMCP.Contracts;

namespace RevitMCP.Server;

internal sealed class ApplyParameterUpdatesOutcome
{
    private ApplyParameterUpdatesOutcome()
    {
    }

    public ApplyParameterUpdatesResult? Result { get; private init; }

    public string? ErrorCode { get; private init; }

    public string? ErrorMessage { get; private init; }

    public bool IsSuccess => Result is not null;

    public static ApplyParameterUpdatesOutcome Success(ApplyParameterUpdatesResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new ApplyParameterUpdatesOutcome { Result = result };
    }

    public static ApplyParameterUpdatesOutcome Failure(string errorCode, string errorMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);
        return new ApplyParameterUpdatesOutcome
        {
            ErrorCode = errorCode,
            ErrorMessage = errorMessage
        };
    }
}
