namespace BimIt.Revit.Server;

internal static class PreviewRoomPatternPlacementsToolMetadata
{
    public const string Name = "revit_preview_room_pattern_placements";

    public const string Title = "Preview Room Pattern Placements";

    public const string Description =
        "Prepare one bounded room-pattern MEP placement batch from trusted room and family-type bindings. This stores a reviewable intent and does not modify Revit. The Assistant calls it. A model must not.";
}
