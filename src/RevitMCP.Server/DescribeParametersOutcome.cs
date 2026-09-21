using RevitMCP.Contracts;

namespace RevitMCP.Server;

internal sealed class DescribeParametersOutcome
{
    private DescribeParametersOutcome()
    {
    }

    public DescribeParametersResult? Result { get; private init; }

    public string? ErrorCode { get; private init; }

    public string? ErrorMessage { get; private init; }

    public IReadOnlyList<InstanceCandidate>? Candidates { get; private init; }

    public bool IsSuccess => Result is not null;

    public static DescribeParametersOutcome Success(DescribeParametersResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new DescribeParametersOutcome { Result = result };
    }

    public static DescribeParametersOutcome Failure(
        string errorCode,
        string errorMessage,
        IReadOnlyList<InstanceCandidate>? candidates = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);
        return new DescribeParametersOutcome
        {
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            Candidates = candidates
        };
    }
}
