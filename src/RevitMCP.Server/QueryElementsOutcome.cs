using RevitMCP.Contracts;

namespace RevitMCP.Server;

internal sealed class QueryElementsOutcome
{
    private QueryElementsOutcome()
    {
    }

    public QueryElementsResult? Result { get; private init; }

    public string? ErrorCode { get; private init; }

    public string? ErrorMessage { get; private init; }

    public IReadOnlyList<InstanceCandidate>? Candidates { get; private init; }

    public bool IsSuccess => Result is not null;

    public static QueryElementsOutcome Success(QueryElementsResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new QueryElementsOutcome { Result = result };
    }

    public static QueryElementsOutcome Failure(
        string errorCode,
        string errorMessage,
        IReadOnlyList<InstanceCandidate>? candidates = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);
        return new QueryElementsOutcome
        {
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            Candidates = candidates
        };
    }
}
