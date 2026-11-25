using System.Text.Json;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Server;

internal sealed class RunCSharpQueryOutcome
{
    private RunCSharpQueryOutcome()
    {
    }

    public RunCSharpQueryResult? Result { get; private init; }

    public string? ErrorCode { get; private init; }

    public string? ErrorMessage { get; private init; }

    public IReadOnlyList<InstanceCandidate>? Candidates { get; private init; }

    public JsonElement? Details { get; private init; }

    public bool IsSuccess => Result is not null;

    public static RunCSharpQueryOutcome Success(RunCSharpQueryResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new RunCSharpQueryOutcome { Result = result };
    }

    public static RunCSharpQueryOutcome Failure(
        string errorCode,
        string errorMessage,
        IReadOnlyList<InstanceCandidate>? candidates = null,
        JsonElement? details = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);
        return new RunCSharpQueryOutcome
        {
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            Candidates = candidates,
            Details = details
        };
    }
}
