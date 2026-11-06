using BimIt.Revit.Contracts;

namespace BimIt.Revit.Server;

internal sealed class QueryParameterBindingsOutcome
{
    private QueryParameterBindingsOutcome()
    {
    }

    public QueryParameterBindingsResult? Result { get; private init; }

    public string? ErrorCode { get; private init; }

    public string? ErrorMessage { get; private init; }

    public IReadOnlyList<InstanceCandidate>? Candidates { get; private init; }

    public bool IsSuccess => Result is not null;

    public static QueryParameterBindingsOutcome Success(QueryParameterBindingsResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new QueryParameterBindingsOutcome { Result = result };
    }

    public static QueryParameterBindingsOutcome Failure(
        string errorCode,
        string errorMessage,
        IReadOnlyList<InstanceCandidate>? candidates = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);
        return new QueryParameterBindingsOutcome
        {
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            Candidates = candidates
        };
    }
}
