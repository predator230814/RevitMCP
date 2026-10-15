using BimIt.Revit.Contracts;

namespace BimIt.Revit.Server;

internal sealed class GetSelectionOutcome
{
    private GetSelectionOutcome()
    {
    }

    public GetSelectionResult? Result { get; private init; }

    public string? ErrorCode { get; private init; }

    public string? ErrorMessage { get; private init; }

    public IReadOnlyList<InstanceCandidate>? Candidates { get; private init; }

    public bool IsSuccess => Result is not null;

    public static GetSelectionOutcome Success(GetSelectionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new GetSelectionOutcome { Result = result };
    }

    public static GetSelectionOutcome Failure(
        string errorCode,
        string errorMessage,
        IReadOnlyList<InstanceCandidate>? candidates = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);
        return new GetSelectionOutcome
        {
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            Candidates = candidates
        };
    }
}
