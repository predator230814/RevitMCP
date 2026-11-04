using BimIt.Revit.Contracts;

namespace BimIt.Revit.Server;

internal sealed class QueryFamilyTypesOutcome
{
    private QueryFamilyTypesOutcome()
    {
    }

    public QueryFamilyTypesResult? Result { get; private init; }

    public string? ErrorCode { get; private init; }

    public string? ErrorMessage { get; private init; }

    public IReadOnlyList<InstanceCandidate>? Candidates { get; private init; }

    public bool IsSuccess => Result is not null;

    public static QueryFamilyTypesOutcome Success(QueryFamilyTypesResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new QueryFamilyTypesOutcome { Result = result };
    }

    public static QueryFamilyTypesOutcome Failure(
        string errorCode,
        string errorMessage,
        IReadOnlyList<InstanceCandidate>? candidates = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);
        return new QueryFamilyTypesOutcome
        {
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            Candidates = candidates
        };
    }
}
