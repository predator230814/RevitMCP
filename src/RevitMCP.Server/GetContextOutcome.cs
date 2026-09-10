using RevitMCP.Contracts;

namespace RevitMCP.Server;

internal sealed class GetContextOutcome
{
    private GetContextOutcome()
    {
    }

    public GetContextResult? Result { get; private init; }

    public string? ErrorCode { get; private init; }

    public string? ErrorMessage { get; private init; }

    public IReadOnlyList<InstanceCandidate>? Candidates { get; private init; }

    public bool IsSuccess => Result is not null;

    public static GetContextOutcome Success(GetContextResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new GetContextOutcome { Result = result };
    }

    public static GetContextOutcome Failure(
        string errorCode,
        string errorMessage,
        IReadOnlyList<InstanceCandidate>? candidates = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);
        return new GetContextOutcome
        {
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            Candidates = candidates
        };
    }
}
