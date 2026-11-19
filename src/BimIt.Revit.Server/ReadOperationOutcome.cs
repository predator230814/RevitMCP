using BimIt.Revit.Contracts;

namespace BimIt.Revit.Server;

internal sealed class ReadOperationOutcome
{
    private ReadOperationOutcome()
    {
    }

    public ReadOperationResult? Result { get; private init; }

    public string? ErrorCode { get; private init; }

    public string? ErrorMessage { get; private init; }

    public IReadOnlyList<InstanceCandidate>? Candidates { get; private init; }

    public bool IsSuccess => Result is not null;

    public static ReadOperationOutcome Success(ReadOperationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new ReadOperationOutcome { Result = result };
    }

    public static ReadOperationOutcome Failure(
        string errorCode,
        string errorMessage,
        IReadOnlyList<InstanceCandidate>? candidates = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);
        return new ReadOperationOutcome
        {
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            Candidates = candidates
        };
    }
}
