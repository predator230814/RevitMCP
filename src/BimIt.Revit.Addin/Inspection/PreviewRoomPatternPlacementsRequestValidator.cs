using BimIt.Revit.Addin.Intents;
using BimIt.Revit.Bridge;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Addin.Inspection;

internal readonly record struct ValidatedRoomPatternPlacement(
    string RoomRef,
    string FamilyTypeRef,
    string Role,
    string PlacementKind,
    double XMetres,
    double YMetres,
    double MountingHeightMetres,
    int QuarterTurns);

internal readonly record struct ValidatedPreviewRoomPatternPlacementsRequest(
    string DocumentId,
    IReadOnlyList<ValidatedRoomPatternPlacement> Placements);

internal static class PreviewRoomPatternPlacementsRequestValidator
{
    public const int MaxRefLength = 128;

    public const double MaxMountingMetres = 6;

    public static ValidatedPreviewRoomPatternPlacementsRequest Validate(PreviewRoomPatternPlacementsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.DocumentId)
            || request.DocumentId.Length > MaxRefLength
            || request.Placements is null
            || request.Placements.Count is < 1 or > PatternPlacementComposer.MaxPlacements)
        {
            throw Invalid();
        }

        var placements = new List<ValidatedRoomPatternPlacement>(request.Placements.Count);
        var rooms = new HashSet<string>(StringComparer.Ordinal);
        foreach (var placement in request.Placements)
        {
            if (placement is null
                || !ValidRef(placement.RoomRef)
                || !ValidRef(placement.FamilyTypeRef)
                || !PatternDeviceRoles.IsRole(placement.Role)
                || !PatternDeviceRoles.IsPlacement(placement.PlacementKind)
                || placement.QuarterTurns is < 0 or > 3
                || !double.IsFinite(placement.XMetres)
                || !double.IsFinite(placement.YMetres)
                || !double.IsFinite(placement.MountingHeightMetres)
                || placement.MountingHeightMetres < 0
                || placement.MountingHeightMetres > MaxMountingMetres)
            {
                throw Invalid();
            }

            rooms.Add(placement.RoomRef!);
            placements.Add(new ValidatedRoomPatternPlacement(
                placement.RoomRef!,
                placement.FamilyTypeRef!,
                placement.Role!,
                placement.PlacementKind!,
                placement.XMetres,
                placement.YMetres,
                placement.MountingHeightMetres,
                placement.QuarterTurns));
        }

        if (rooms.Count > PatternPlacementComposer.MaxRooms)
        {
            throw Invalid();
        }

        return new ValidatedPreviewRoomPatternPlacementsRequest(request.DocumentId, placements);
    }

    private static bool ValidRef(string? value) => value is not null && value.Length is >= 1 and <= MaxRefLength;

    private static BridgeException Invalid()
    {
        return new BridgeException(
            CapabilityErrorCodes.InvalidRoomPatternPlacement,
            "The room pattern placement preview is invalid.");
    }
}
