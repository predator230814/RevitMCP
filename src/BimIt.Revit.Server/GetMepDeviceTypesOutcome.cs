using BimIt.Revit.Contracts;

namespace BimIt.Revit.Server;

internal sealed class GetMepDeviceTypesOutcome
{
    private GetMepDeviceTypesOutcome()
    {
    }

    public GetMepDeviceTypesResult? Result { get; private init; }

    public string? ErrorCode { get; private init; }

    public string? ErrorMessage { get; private init; }

    public IReadOnlyList<InstanceCandidate>? Candidates { get; private init; }

    public bool IsSuccess => Result is not null;

    public static GetMepDeviceTypesOutcome Success(GetMepDeviceTypesResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new GetMepDeviceTypesOutcome { Result = result };
    }

    public static GetMepDeviceTypesOutcome Failure(
        string errorCode,
        string errorMessage,
        IReadOnlyList<InstanceCandidate>? candidates = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);
        return new GetMepDeviceTypesOutcome
        {
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            Candidates = candidates
        };
    }
}
