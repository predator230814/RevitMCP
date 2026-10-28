using BimIt.Revit.Bridge;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Addin.Inspection;

internal readonly record struct ValidatedReadSpatialRoomsRequest(string DocumentId, string? NameContains, int Limit);

internal static class ReadSpatialRoomsRequestValidator
{
    public const int MaxLimit = 100;

    public static ValidatedReadSpatialRoomsRequest Validate(ReadSpatialRoomsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.DocumentId is null || request.DocumentId.Length is < 1 or > RoomLayoutLimits.MaxRefLength)
        {
            throw Invalid();
        }

        if (request.NameContains is not null && !IsName(request.NameContains))
        {
            throw Invalid();
        }

        if (request.Limit is < 1 or > MaxLimit)
        {
            throw Invalid();
        }

        return new ValidatedReadSpatialRoomsRequest(request.DocumentId, request.NameContains, request.Limit ?? MaxLimit);
    }

    private static bool IsName(string value)
    {
        return value.Length is >= 1 and <= 40
            && value.All(character => char.IsLetterOrDigit(character) || character is ' ' or '-' or '\'');
    }

    private static BridgeException Invalid()
    {
        return new BridgeException(CapabilityErrorCodes.InvalidSpatialRooms, "The spatial room request is not valid.");
    }
}
