using BimIt.Revit.Contracts;

namespace BimIt.Revit.Server;

internal sealed class ReadSpatialRoomsOutcome
{
    private ReadSpatialRoomsOutcome()
    {
    }

    public ReadSpatialRoomsResult? Result { get; private init; }

    public string? ErrorCode { get; private init; }

    public string? ErrorMessage { get; private init; }

    public IReadOnlyList<InstanceCandidate>? Candidates { get; private init; }

    public bool IsSuccess => Result is not null;

    public static ReadSpatialRoomsOutcome Success(ReadSpatialRoomsResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new ReadSpatialRoomsOutcome { Result = result };
    }

    public static ReadSpatialRoomsOutcome Failure(string errorCode, string errorMessage, IReadOnlyList<InstanceCandidate>? candidates = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);
        return new ReadSpatialRoomsOutcome { ErrorCode = errorCode, ErrorMessage = errorMessage, Candidates = candidates };
    }
}
