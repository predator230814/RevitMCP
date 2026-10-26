using BimIt.Revit.Contracts;
using Xunit;

namespace BimIt.Revit.Contracts.Tests;

public sealed class SpatialRoomRefTests
{
    [Fact]
    public void Host_and_link_refs_stay_opaque_and_do_not_carry_paths()
    {
        var host = SpatialRoomRefCodec.Host("room-unique-id");
        Assert.StartsWith("sr1.h.", host, StringComparison.Ordinal);
        Assert.True(SpatialRoomRefCodec.TryHost(host, out var uniqueId));
        Assert.Equal("room-unique-id", uniqueId);
        Assert.False(SpatialRoomRefCodec.IsLink(host));

        var link = SpatialRoomRefCodec.Link("link-unique-id", "linked-room-id");
        Assert.StartsWith("sr1.l.", link, StringComparison.Ordinal);
        Assert.True(SpatialRoomRefCodec.IsLink(link));
        Assert.False(SpatialRoomRefCodec.TryHost(link, out _));
        Assert.DoesNotContain(".rvt", link, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\\", link, StringComparison.Ordinal);
        Assert.DoesNotContain("/", link, StringComparison.Ordinal);
    }

    [Fact]
    public void Link_label_keeps_a_display_name_and_drops_the_path()
    {
        Assert.Equal("Snowdon Towers", SpatialLinkLabel.Sanitize(@"C:\Models\Snowdon Towers.rvt"));
        Assert.Equal("Architecture", SpatialLinkLabel.Sanitize("/server/share/Architecture.RVT:3"));
        Assert.Equal("Link", SpatialLinkLabel.Sanitize(@"C:\secret\..\:bad"));
        Assert.Equal("Link", SpatialLinkLabel.Sanitize(null));
    }

    [Fact]
    public void Plan_frame_applies_translation_and_rotation()
    {
        var translated = new SpatialPlanFrame(10, 4, 1, 0, 0, 1);
        Assert.Equal((12, 7), translated.Apply(2, 3));
        Assert.Equal((1, 0), translated.ApplyVector(1, 0));

        var rotated = new SpatialPlanFrame(0, 0, 0, 1, -1, 0);
        Assert.Equal((-3, 2), rotated.Apply(2, 3));
        Assert.Equal((0, 1), rotated.ApplyVector(1, 0));
        Assert.Equal((2, 3), SpatialPlanFrame.Identity.Apply(2, 3));
    }
}
