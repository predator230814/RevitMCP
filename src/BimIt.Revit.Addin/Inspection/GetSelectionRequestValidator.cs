using BimIt.Revit.Bridge;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Addin.Inspection;

internal readonly record struct ValidatedGetSelectionRequest(string? DocumentId, int Limit);

internal static class GetSelectionRequestValidator
{
    public const int DefaultLimit = 25;
    public const int MinLimit = 1;
    public const int MaxLimit = 100;

    public static ValidatedGetSelectionRequest Validate(GetSelectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var limit = request.Limit ?? DefaultLimit;
        if (limit is < MinLimit or > MaxLimit)
        {
            throw new BridgeException(
                CapabilityErrorCodes.InvalidSelection,
                "The selection request is invalid.");
        }

        return new ValidatedGetSelectionRequest(request.DocumentId, limit);
    }
}
