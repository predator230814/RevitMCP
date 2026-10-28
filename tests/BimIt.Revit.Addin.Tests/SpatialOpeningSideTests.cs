using BimIt.Revit.Addin.Capabilities;
using Xunit;

namespace BimIt.Revit.Addin.Tests;

public sealed class SpatialOpeningSideTests
{
    [Fact]
    public void Door_other_side_keeps_a_room_label_and_drops_a_path()
    {
        var corridor = RevitReadSpatialRoomsService.DescribeSide(true, false, true, "Corridor", "C1");
        Assert.Equal("room", corridor.Status);
        Assert.Equal("Corridor", corridor.Name);
        Assert.Equal("C1", corridor.Number);

        var outside = RevitReadSpatialRoomsService.DescribeSide(false, true, false, null, null);
        Assert.Equal("outside", outside.Status);
        Assert.Null(outside.Name);

        var path = RevitReadSpatialRoomsService.DescribeSide(true, false, true, "C:\\Links\\Snowdon.rvt", "1");
        Assert.Equal("unknown", path.Status);
        Assert.Null(path.Name);

        var unknown = RevitReadSpatialRoomsService.DescribeSide(false, false, true, "Corridor", "1");
        Assert.Equal("unknown", unknown.Status);
    }
}
