using BimIt.Revit.Contracts;

namespace BimIt.Revit.Server;

internal sealed class ApplyMepDevicePlacementsOutcome
{
    private ApplyMepDevicePlacementsOutcome()
    {
    }

    public ApplyMepDevicePlacementsResult? Result { get; private init; }

    public string? ErrorCode { get; private init; }

    public string? ErrorMessage { get; private init; }

    public bool IsSuccess => Result is not null;

    public static ApplyMepDevicePlacementsOutcome Success(ApplyMepDevicePlacementsResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new ApplyMepDevicePlacementsOutcome { Result = result };
    }

    public static ApplyMepDevicePlacementsOutcome Failure(string errorCode, string errorMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);
        return new ApplyMepDevicePlacementsOutcome { ErrorCode = errorCode, ErrorMessage = errorMessage };
    }
}
