using RevitMCP.Contracts;

namespace RevitMCP.Server;

internal sealed class PreviewParameterUpdatesOutcome
{
    private PreviewParameterUpdatesOutcome()
    {
    }

    public PreviewParameterUpdatesResult? Result { get; private init; }

    public string? ErrorCode { get; private init; }

    public string? ErrorMessage { get; private init; }

    public IReadOnlyList<InstanceCandidate>? Candidates { get; private init; }

    public bool IsSuccess => Result is not null;

    public static PreviewParameterUpdatesOutcome Success(PreviewParameterUpdatesResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new PreviewParameterUpdatesOutcome { Result = result };
    }

    public static PreviewParameterUpdatesOutcome Failure(
        string errorCode,
        string errorMessage,
        IReadOnlyList<InstanceCandidate>? candidates = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);
        return new PreviewParameterUpdatesOutcome
        {
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            Candidates = candidates
        };
    }
}
