using RevitMCP.Contracts;

namespace RevitMCP.Server;

internal sealed class GetMepTopologyOutcome
{
    private GetMepTopologyOutcome()
    {
    }

    public GetMepTopologyResult? Result { get; private init; }

    public string? ErrorCode { get; private init; }

    public string? ErrorMessage { get; private init; }

    public IReadOnlyList<InstanceCandidate>? Candidates { get; private init; }

    public bool IsSuccess => Result is not null;

    public static GetMepTopologyOutcome Success(GetMepTopologyResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new GetMepTopologyOutcome { Result = result };
    }

    public static GetMepTopologyOutcome Failure(
        string errorCode,
        string errorMessage,
        IReadOnlyList<InstanceCandidate>? candidates = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);
        return new GetMepTopologyOutcome
        {
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            Candidates = candidates
        };
    }
}
