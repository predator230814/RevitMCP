using RevitMCP.Contracts;

namespace RevitMCP.Server;

internal sealed class GetWarningsOutcome
{
    private GetWarningsOutcome()
    {
    }

    public GetWarningsResult? Result { get; private init; }

    public string? ErrorCode { get; private init; }

    public string? ErrorMessage { get; private init; }

    public IReadOnlyList<InstanceCandidate>? Candidates { get; private init; }

    public bool IsSuccess => Result is not null;

    public static GetWarningsOutcome Success(GetWarningsResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new GetWarningsOutcome { Result = result };
    }

    public static GetWarningsOutcome Failure(
        string errorCode,
        string errorMessage,
        IReadOnlyList<InstanceCandidate>? candidates = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);
        return new GetWarningsOutcome
        {
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            Candidates = candidates
        };
    }
}
