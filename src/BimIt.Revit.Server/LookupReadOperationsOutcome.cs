using BimIt.Revit.Contracts;

namespace BimIt.Revit.Server;

internal sealed class LookupReadOperationsOutcome
{
    private LookupReadOperationsOutcome()
    {
    }

    public LookupReadOperationsResult? Result { get; private init; }

    public string? ErrorCode { get; private init; }

    public string? ErrorMessage { get; private init; }

    public IReadOnlyList<InstanceCandidate>? Candidates { get; private init; }

    public bool IsSuccess => Result is not null;

    public static LookupReadOperationsOutcome Success(LookupReadOperationsResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new LookupReadOperationsOutcome { Result = result };
    }

    public static LookupReadOperationsOutcome Failure(
        string errorCode,
        string errorMessage,
        IReadOnlyList<InstanceCandidate>? candidates = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);
        return new LookupReadOperationsOutcome
        {
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            Candidates = candidates
        };
    }
}
