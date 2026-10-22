using System.ComponentModel;
using System.Text.Json;
using BimIt.Revit.Assistant.Patterns;
using BimIt.Revit.Assistant.Providers.GitHubCopilot;
using Xunit;

namespace BimIt.Revit.Assistant.Tests;

public sealed class PageFrameTests
{
    private const string InstanceId = "11111111-2222-3333-4444-555555555555";

    [Fact]
    public void Landscape_and_portrait_pages_keep_the_same_room_aspect()
    {
        var landscape = Embed(Rectangle(50, 50, 250, 150), null, new PlanPoint(150, 100), 400, 200);
        var portrait = Embed(Rectangle(0, 150, 200, 250), null, new PlanPoint(100, 200), 200, 400);
        var landscapeGeometry = PageFrame.ForMatching(landscape, 400, 200);
        var portraitGeometry = PageFrame.ForMatching(portrait, 200, 400);

        Assert.Equal(2, Aspect(landscapeGeometry.Boundary), 3);
        Assert.Equal(2, Aspect(portraitGeometry.Boundary), 3);
        Assert.NotEqual(400d / 200d, 200d / 400d);

        var room = RectangleRoom(10, 20, 18, 24, null);
        var wide = Assert.Single(RoomShapeMatcher.Match(landscapeGeometry, [room]));
        var tall = Assert.Single(RoomShapeMatcher.Match(portraitGeometry, [room]));
        Assert.Equal("high", wide.Band);
        Assert.Equal("high", tall.Band);
        Assert.Equal(0, wide.RotationDegrees);
        Assert.Equal(0, tall.RotationDegrees);
        Assert.False(wide.Mirrored);
        Assert.False(tall.Mirrored);
    }

    [Fact]
    public void Page_y_down_is_converted_once_and_keeps_an_asymmetric_room_unmirrored()
    {
        var top = PageFrame.ToCartesian(0.25, 0, 400, 200);
        var bottom = PageFrame.ToCartesian(0.25, 1, 400, 200);
        Assert.Equal(100, top.X);
        Assert.Equal(200, top.Y);
        Assert.Equal(0, bottom.Y);

        var page = EmbedOn(L(false), new PlanPoint(3, 0), new PlanPoint(2, 3), 300, 180);
        var geometry = PageFrame.ForMatching(page, 300, 180);
        Assert.Equal(110, geometry.Boundary.Max(point => point.Y), 6);
        Assert.Equal(30, geometry.Boundary.Min(point => point.Y), 6);

        var direct = Cartesian(L(false), new PlanPoint(3, 0), new PlanPoint(2, 3));
        var room = ShapeRoom(Translate(L(false), 30, 80), null);
        var fromPage = Assert.Single(RoomShapeMatcher.Match(geometry, [room]));
        var fromCartesian = Assert.Single(RoomShapeMatcher.Match(direct, [room]));
        Assert.Equal("high", fromPage.Band);
        Assert.False(fromPage.Mirrored);
        Assert.Equal(fromCartesian.Mirrored, fromPage.Mirrored);
        Assert.Equal(fromCartesian.RotationDegrees, fromPage.RotationDegrees);
        Assert.Equal(fromCartesian.Devices[0].NormalizedX, fromPage.Devices[0].NormalizedX);
        Assert.Equal(fromCartesian.Devices[0].NormalizedY, fromPage.Devices[0].NormalizedY);
    }

    [Fact]
    public void A_mirrored_asymmetric_room_stays_mirrored_after_the_page_conversion()
    {
        var page = EmbedOn(L(false), null, new PlanPoint(2, 3), 300, 180);
        var geometry = PageFrame.ForMatching(page, 300, 180);
        var room = ShapeRoom(Translate(L(true), 30, 80), null);
        var match = Assert.Single(RoomShapeMatcher.Match(geometry, [room]));
        Assert.Equal("high", match.Band);
        Assert.True(match.Mirrored);
        Assert.True(match.Score >= RoomShapeMatcher.HighConfidence);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Rotated_asymmetric_rooms_keep_their_quarter_turn(int quarters)
    {
        var page = EmbedOn(L(false), null, new PlanPoint(2, 3), 300, 180);
        var geometry = PageFrame.ForMatching(page, 300, 180);
        var room = ShapeRoom(Translate(Rotate(L(false), quarters), 40, 20), null);
        var match = Assert.Single(RoomShapeMatcher.Match(geometry, [room]));
        Assert.Equal("high", match.Band);
        Assert.False(match.Mirrored);
        Assert.Equal(quarters * 90, match.RotationDegrees);
    }

    [Fact]
    public void Entrance_and_device_projection_use_the_cartesian_page_frame()
    {
        var page = PagePattern(Rectangle(0, 0, 4, 2), new PlanPoint(2, 0), new PlanPoint(1, 0.4), 400, 200, 100, 0, 0);
        var geometry = PageFrame.ForMatching(page, 400, 200);
        Assert.Equal(new PlanPoint(200, 0), geometry.Entrance!.Value);
        Assert.Equal(100, geometry.Devices[0].X, 6);
        Assert.Equal(40, geometry.Devices[0].Y, 6);

        var south = Assert.Single(RoomShapeMatcher.Match(geometry, [RectangleRoom(0, 0, 4, 2, new PlanPoint(2, 0))]));
        Assert.Equal(0, south.RotationDegrees);
        Assert.False(south.Mirrored);
        Assert.Equal(0.2, south.Devices[0].NormalizedY, 3);

        var north = Assert.Single(RoomShapeMatcher.Match(geometry, [RectangleRoom(0, 0, 4, 2, new PlanPoint(2, 2))]));
        Assert.Equal(180, north.RotationDegrees);
        Assert.False(north.Mirrored);
        Assert.Equal(0.8, north.Devices[0].NormalizedY, 3);
    }

    [Fact]
    public async Task Source_overlay_keeps_image_coordinates_and_hides_identities()
    {
        var artifact = new AssistantDocumentArtifact(
            "sketch.png",
            AssistantDocumentKind.Image,
            4,
            null,
            false,
            [new AssistantVisualPage(1, "image/png", [1, 2, 3, 4], 400, 200)],
            false,
            1);
        var store = new RoomPatternStore();
        var tool = new RoomPatternProposalTool(new ClosedSession(), InstanceId, store, () => artifact);
        var result = await tool.InvokeAsync(Json("""
            {
              "page_number": 1,
              "boundary": [{"x":0.125,"y":0.75},{"x":0.625,"y":0.75},{"x":0.625,"y":0.25},{"x":0.125,"y":0.25}],
              "entrance": {"x":0.5,"y":1},
              "devices": [{"role":"receptacle","page_x":0.25,"page_y":0.8,"placement_kind":"interior"}]
            }
            """), CancellationToken.None);

        var model = JsonSerializer.Serialize(result);
        Assert.Equal(true, result["stored"]);
        Assert.DoesNotContain("element_ref", model, StringComparison.Ordinal);
        Assert.DoesNotContain("instance_id", model, StringComparison.Ordinal);
        Assert.DoesNotContain("document_id", model, StringComparison.Ordinal);
        var review = JsonDocument.Parse(store.PeekReviewJson()!);
        var boundary = review.RootElement.GetProperty("boundary")[0];
        Assert.Equal(0.125, boundary.GetProperty("x").GetDouble());
        Assert.Equal(0.75, boundary.GetProperty("y").GetDouble());
        var device = review.RootElement.GetProperty("devices")[0];
        Assert.Equal(0.25, device.GetProperty("x").GetDouble());
        Assert.Equal(0.8, device.GetProperty("y").GetDouble());
        Assert.Equal(1, review.RootElement.GetProperty("entrance").GetProperty("y").GetDouble());
        Assert.DoesNotContain("element_ref", review.RootElement.GetRawText(), StringComparison.Ordinal);
        Assert.Equal(7, AssistantModelTools.Names.Length);
        Assert.DoesNotContain(AssistantTools.GetRoomLayouts, AssistantModelTools.Names);
        Assert.False(AssistantTools.IsTrustedWriteTool(AssistantTools.GetRoomLayouts));
        Assert.False(AssistantTools.IsTrustedWriteTool(RoomPatternProposalTool.Name));
        Assert.Contains("left to right", GitHubCopilotSessionBlueprint.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains("top to bottom", GitHubCopilotSessionBlueprint.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains("top to bottom", Description(typeof(RoomPatternPointRequest), "Y"), StringComparison.Ordinal);
        Assert.Contains("left to right", Description(typeof(RoomPatternPointRequest), "X"), StringComparison.Ordinal);
        Assert.Contains("top to bottom", Description(typeof(RoomPatternDeviceRequest), "PageY"), StringComparison.Ordinal);
        Assert.Contains("left to right", Description(typeof(RoomPatternDeviceRequest), "PageX"), StringComparison.Ordinal);
    }

    private static string Description(Type type, string property)
    {
        return type.GetProperty(property)!.GetCustomAttributes(typeof(DescriptionAttribute), false).Cast<DescriptionAttribute>().Single().Description;
    }

    private static SourceRoomPattern Embed(IReadOnlyList<PlanPoint> pixels, PlanPoint? entrance, PlanPoint device, int width, int height)
    {
        return PagePattern(pixels, entrance, device, width, height, 1, 0, 0);
    }

    private static SourceRoomPattern EmbedOn(
        IReadOnlyList<PlanPoint> cartesian,
        PlanPoint? entrance,
        PlanPoint device,
        int width,
        int height)
    {
        return PagePattern(cartesian, entrance, device, width, height, 20, 20, 30);
    }

    private static SourceRoomPattern PagePattern(
        IReadOnlyList<PlanPoint> cartesian,
        PlanPoint? entrance,
        PlanPoint device,
        int width,
        int height,
        double scale,
        double originX,
        double originY)
    {
        PlanPoint Page(PlanPoint point)
        {
            var pixelX = originX + (point.X * scale);
            var pixelY = originY + (point.Y * scale);
            return new PlanPoint(pixelX / width, 1 - (pixelY / height));
        }

        return new SourceRoomPattern(
            cartesian.Select(Page).ToArray(),
            entrance is PlanPoint door ? Page(door) : null,
            [new SourceDevice("receptacle", Page(device).X, Page(device).Y, "interior", null)]);
    }

    private static SourceRoomPattern Cartesian(IReadOnlyList<PlanPoint> boundary, PlanPoint? entrance, PlanPoint device)
    {
        return new SourceRoomPattern(boundary, entrance, [new SourceDevice("receptacle", device.X, device.Y, "interior", null)]);
    }

    private static CandidateRoom RectangleRoom(double x0, double y0, double x1, double y1, PlanPoint? door)
    {
        return ShapeRoom(Rectangle(x0, y0, x1, y1), door);
    }

    private static CandidateRoom ShapeRoom(IReadOnlyList<PlanPoint> loop, PlanPoint? door)
    {
        var centroid = new PlanPoint(loop.Average(point => point.X), loop.Average(point => point.Y));
        var openings = door is PlanPoint point ? new[] { new CandidateOpening(point.X, point.Y, "door") } : Array.Empty<CandidateOpening>();
        return new CandidateRoom("Bedroom", "201", "Level 2", centroid.X, centroid.Y, loop, false, false, openings);
    }

    private static IReadOnlyList<PlanPoint> Rectangle(double x0, double y0, double x1, double y1)
    {
        return [new PlanPoint(x0, y0), new PlanPoint(x1, y0), new PlanPoint(x1, y1), new PlanPoint(x0, y1)];
    }

    private static PlanPoint[] L(bool mirrored)
    {
        PlanPoint[] shape = [new(0, 0), new(6, 0), new(6, 2), new(4, 2), new(4, 4), new(0, 4)];
        if (mirrored)
        {
            shape = shape.Select(point => new PlanPoint(6 - point.X, point.Y)).Reverse().ToArray();
        }

        return shape;
    }

    private static PlanPoint[] Translate(IReadOnlyList<PlanPoint> shape, double x, double y)
    {
        return shape.Select(point => new PlanPoint(point.X + x, point.Y + y)).ToArray();
    }

    private static PlanPoint[] Rotate(IReadOnlyList<PlanPoint> shape, int quarters)
    {
        return shape.Select(point =>
        {
            var x = point.X;
            var y = point.Y;
            for (var turn = 0; turn < quarters; turn++)
            {
                var nextX = -y;
                var nextY = x;
                x = nextX;
                y = nextY;
            }

            return new PlanPoint(x, y);
        }).ToArray();
    }

    private static double Aspect(IReadOnlyList<PlanPoint> loop)
    {
        var width = loop.Max(point => point.X) - loop.Min(point => point.X);
        var height = loop.Max(point => point.Y) - loop.Min(point => point.Y);
        return width / height;
    }

    private static JsonElement Json(string json) => JsonSerializer.Deserialize<JsonElement>(json);

    private sealed class ClosedSession : IAssistantToolSession
    {
        public Task<AssistantMcpReadiness> InitializeAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new AssistantMcpReadiness(false, 15, [], null));

        public Task<AssistantToolCall> CallAsync(string toolName, IReadOnlyDictionary<string, object?> arguments, CancellationToken cancellationToken) =>
            Task.FromResult(new AssistantToolCall(true, "NO_REVIT_INSTANCE", "none", null));
    }
}
