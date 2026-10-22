namespace BimIt.Revit.Server;

internal static class GetRoomLayoutsToolMetadata
{
    public const string Name = "revit_get_room_layouts";

    public const string Title = "Get Revit Room Layouts";

    public const string Description =
        "Read bounded plan geometry for 1 to 20 Rooms in the active document. Coordinates are metres. Non-room references are rejected per item. This does not modify Revit.";
}
