using BimIt.Revit.Contracts;

namespace BimIt.Revit.Server;

internal sealed class PreviewMepDevicePlacementsOutcome
{
    private PreviewMepDevicePlacementsOutcome()
    {
    }

    public PreviewMepDevicePlacementsResult? Result { get; private init; }

    public string? ErrorCode { get; private init; }

    public string? ErrorMessage { get; private init; }

    public IReadOnlyList<InstanceCandidate>? Candidates { get; private init; }

    public bool IsSuccess => Result is not null;

    public static PreviewMepDevicePlacementsOutcome Success(PreviewMepDevicePlacementsResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new PreviewMepDevicePlacementsOutcome { Result = result };
    }

    public static PreviewMepDevicePlacementsOutcome Failure(
        string errorCode,
        string errorMessage,
        IReadOnlyList<InstanceCandidate>? candidates = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);
        return new PreviewMepDevicePlacementsOutcome
        {
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            Candidates = candidates
        };
    }
}
