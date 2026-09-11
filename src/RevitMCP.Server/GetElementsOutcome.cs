using RevitMCP.Contracts;

namespace RevitMCP.Server;

internal sealed class GetElementsOutcome
{
    private GetElementsOutcome()
    {
    }

    public GetElementsResult? Result { get; private init; }

    public string? ErrorCode { get; private init; }

    public string? ErrorMessage { get; private init; }

    public IReadOnlyList<InstanceCandidate>? Candidates { get; private init; }

    public bool IsSuccess => Result is not null;

    public static GetElementsOutcome Success(GetElementsResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new GetElementsOutcome { Result = result };
    }

    public static GetElementsOutcome Failure(
        string errorCode,
        string errorMessage,
        IReadOnlyList<InstanceCandidate>? candidates = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);
        return new GetElementsOutcome
        {
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            Candidates = candidates
        };
    }
}
