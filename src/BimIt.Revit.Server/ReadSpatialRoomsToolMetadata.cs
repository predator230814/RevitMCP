namespace BimIt.Revit.Server;

internal static class ReadSpatialRoomsToolMetadata
{
    public const string Name = "revit_read_spatial_rooms";

    public const string Title = "Read Spatial Rooms";

    public const string Description =
        "Read a bounded set of Rooms from the host model and from loaded top-level Revit links. Geometry is returned in host coordinates. Linked room identity stays opaque. Unloaded links are reported unavailable. This does not modify Revit.";
}
