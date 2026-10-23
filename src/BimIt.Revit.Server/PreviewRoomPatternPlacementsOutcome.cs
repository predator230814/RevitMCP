using BimIt.Revit.Contracts;

namespace BimIt.Revit.Server;

internal sealed class PreviewRoomPatternPlacementsOutcome
{
    private PreviewRoomPatternPlacementsOutcome()
    {
    }

    public PreviewRoomPatternPlacementsResult? Result { get; private init; }

    public string? ErrorCode { get; private init; }

    public string? ErrorMessage { get; private init; }

    public IReadOnlyList<InstanceCandidate>? Candidates { get; private init; }

    public bool IsSuccess => Result is not null;

    public static PreviewRoomPatternPlacementsOutcome Success(PreviewRoomPatternPlacementsResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new PreviewRoomPatternPlacementsOutcome { Result = result };
    }

    public static PreviewRoomPatternPlacementsOutcome Failure(
        string errorCode,
        string errorMessage,
        IReadOnlyList<InstanceCandidate>? candidates = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);
        return new PreviewRoomPatternPlacementsOutcome
        {
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            Candidates = candidates
        };
    }
}
