using System.Text.Json;
using BimIt.Revit.Assistant;
using BimIt.Revit.Assistant.Ipc;
using BimIt.Revit.Assistant.Patterns;
using Xunit;

namespace BimIt.Revit.Assistant.Tests;

public sealed class SemanticPatternTests
{
    [Fact]
    public void Outer_loop_is_the_largest_and_holes_are_preserved()
    {
        var outer = StudioOuter();
        var hole = Rectangle(2, 1, 3, 2);
        Assert.True(BoundaryLoopSelector.TrySelect([hole, outer], out var selected, out var holes, out var reason));
        Assert.Null(reason);
        Assert.Equal(outer, selected);
        Assert.Single(holes);

        Assert.False(BoundaryLoopSelector.TrySelect([Rectangle(0, 0, 4, 4), Rectangle(0.2, 0.2, 4.1, 4.1)], out _, out _, out var ambiguous));
        Assert.Equal(BoundaryLoopSelector.AmbiguousOuterLoop, ambiguous);
        Assert.False(BoundaryLoopSelector.TrySelect([[new PlanPoint(0, 0), new PlanPoint(1, 0)]], out _, out _, out var missing));
        Assert.Equal(BoundaryLoopSelector.NoOuterLoop, missing);
    }

    [Fact]
    public void Studio_with_an_inner_loop_is_not_rejected_for_multiple_loops()
    {
        var source = Pattern(Rectangle(0, 0, 6, 4), new PlanPoint(3, 0), []);
        var room = new CandidateRoom(
            "Studio",
            "12",
            "Level 1",
            3,
            2,
            Rectangle(0, 0, 6, 4),
            false,
            [new CandidateOpening(3, 0, "door")],
            Holes: [Rectangle(2, 1, 3, 2)]);
        var match = Assert.Single(RoomShapeMatcher.Match(source, [room]));
        Assert.NotEqual("multiple_boundary_loops", match.Reason);
        Assert.NotEqual("rejected", match.Band);
    }

    [Fact]
    public void Understood_summary_describes_rules_without_coordinates()
    {
        var lines = PatternUnderstanding.Summarize(
            [
                new PatternRule("switch", PatternAnchors.Entrance, 0.15, null, 0.8),
                new PatternRule("receptacle", PatternAnchors.PairedSymmetric, 0.6, "pair", 0.7),
                new PatternRule("luminaire", PatternAnchors.RoomCenter, 0, null, 0.9),
                new PatternRule("data", PatternAnchors.OppositeBoundary, 0.2, null, 0.6),
            ],
            [new PatternAnchor("bed", 0.5, 0.5)]);
        var text = string.Join(" ", lines);
        Assert.Contains("1 switch associated with the entrance", text, StringComparison.Ordinal);
        Assert.Contains("2 receptacles forming a symmetric pair", text, StringComparison.Ordinal);
        Assert.Contains("1 ceiling luminaire near room centre", text, StringComparison.Ordinal);
        Assert.Contains("1 data/TV device on an opposite wall", text, StringComparison.Ordinal);
        Assert.Contains("Saw a visual anchor: bed", text, StringComparison.Ordinal);
        Assert.DoesNotContain("page_", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Entrance_centre_opposite_and_pair_adapt_to_the_target()
    {
        var rules = new[]
        {
            new PatternRule("switch", PatternAnchors.Entrance, 0.15, null, null),
            new PatternRule("luminaire", PatternAnchors.RoomCenter, 0, null, null),
            new PatternRule("data", PatternAnchors.OppositeBoundary, 0.2, null, null),
            new PatternRule("receptacle", PatternAnchors.PairedSymmetric, 0.6, "pair", null),
        };
        var devices = new[]
        {
            new SourceDevice("switch", 0.5, 0.05, "boundary", null),
            new SourceDevice("luminaire", 0.5, 0.5, "interior", null),
            new SourceDevice("data", 0.5, 0.95, "boundary", null),
            new SourceDevice("receptacle", 0.3, 0.95, "boundary", null),
            new SourceDevice("receptacle", 0.7, 0.95, "boundary", null),
        };
        var source = new SourceRoomPattern(Rectangle(0, 0, 1, 1), new PlanPoint(0.5, 0), devices, rules);
        var room = new CandidateRoom("Bedroom", "2", "L1", 5, 3, Rectangle(0, 0, 10, 6), false, [new CandidateOpening(1, 0, "door")]);
        var match = Assert.Single(RoomShapeMatcher.Match(source, [room]));
        Assert.NotEqual("rejected", match.Band);
        var byRole = match.Devices.GroupBy(device => device.Role).ToDictionary(group => group.Key, group => group.ToArray());
        Assert.InRange(byRole["switch"][0].LocalY, 0, 1.5);
        Assert.InRange(byRole["switch"][0].LocalX, 0, 2.5);
        Assert.InRange(byRole["luminaire"][0].LocalX, 4.5, 5.5);
        Assert.InRange(byRole["luminaire"][0].LocalY, 2.5, 3.5);
        Assert.True(byRole["data"][0].LocalY > 4);
        Assert.Equal(2, byRole["receptacle"].Length);
        Assert.True(byRole["receptacle"].All(device => device.LocalY > 4));
    }

    [Fact]
    public void Rotated_room_keeps_the_switch_at_its_own_entrance()
    {
        var source = new SourceRoomPattern(
            Rectangle(0, 0, 4, 2),
            new PlanPoint(2, 0),
            [new SourceDevice("switch", 2, 0.2, "boundary", null)],
            [new PatternRule("switch", PatternAnchors.Entrance, 0.15, null, null)]);
        var room = new CandidateRoom("Bedroom", "9", "L1", 1, 2, Rectangle(0, 0, 2, 4), false, [new CandidateOpening(1, 0, "door")]);
        var match = Assert.Single(RoomShapeMatcher.Match(source, [room]));
        var device = Assert.Single(match.Devices);
        Assert.InRange(device.LocalX, 0.2, 1.8);
        Assert.InRange(device.LocalY, 0, 1);
        Assert.NotEqual("unresolved_rule", match.Reason);
    }

    [Fact]
    public void Different_geometry_can_still_resolve_semantic_rules()
    {
        var source = new SourceRoomPattern(
            Rectangle(0, 0, 4, 2),
            new PlanPoint(2, 0),
            [new SourceDevice("switch", 2, 0.2, "boundary", null), new SourceDevice("luminaire", 2, 1, "interior", null)],
            [
                new PatternRule("switch", PatternAnchors.Entrance, 0.15, null, null),
                new PatternRule("luminaire", PatternAnchors.RoomCenter, 0, null, null),
            ]);
        var room = new CandidateRoom("Bedroom", "4", "L1", 4, 1, Rectangle(0, 0, 8, 2), false, [new CandidateOpening(1, 0, "door")]);
        var match = Assert.Single(RoomShapeMatcher.Match(source, [room]));
        Assert.Equal("lower", match.Band);
        Assert.Equal("semantic_match", match.Reason);
        Assert.Equal(2, match.Devices.Count);
    }

    [Fact]
    public void Missing_entrance_stays_unresolved()
    {
        var source = new SourceRoomPattern(
            Rectangle(0, 0, 4, 2),
            new PlanPoint(2, 0),
            [new SourceDevice("switch", 2, 0.2, "boundary", null)],
            [new PatternRule("switch", PatternAnchors.Entrance, 0.15, null, null)]);
        var room = new CandidateRoom("Bedroom", "5", "L1", 2, 1, Rectangle(0, 0, 4, 2), false, []);
        var match = Assert.Single(RoomShapeMatcher.Match(source, [room]));
        Assert.Equal("rejected", match.Band);
        Assert.Equal("unresolved_rule", match.Reason);
    }

    [Fact]
    public void A_point_inside_a_hole_does_not_become_a_placement()
    {
        var source = new SourceRoomPattern(
            Rectangle(0, 0, 10, 8),
            null,
            [new SourceDevice("luminaire", 5, 4, "interior", null)],
            [new PatternRule("luminaire", PatternAnchors.RoomCenter, 0, null, null)]);
        var room = new CandidateRoom("Bedroom", "6", "L1", 5, 4, Rectangle(0, 0, 10, 8), false, [], Holes: [Rectangle(4, 3, 6, 5)]);
        var match = Assert.Single(RoomShapeMatcher.Match(source, [room]));
        Assert.Equal("unresolved_rule", match.Reason);
    }

    [Fact]
    public async Task Rules_reject_coordinates_and_accept_the_small_vocabulary()
    {
        var inventedStore = new RoomPatternStore();
        var invented = await StoredTool(inventedStore).InvokeAsync(Json(Body("""
            "rules":[{"role":"switch","anchor":"entrance","x":1.2,"y":3.4}]
            """)), CancellationToken.None);
        Assert.Equal(true, invented["stored"]);
        Assert.Equal(SemanticReport.None, invented["semantic_status"]);
        Assert.Contains(SemanticWarningCodes.MalformedRule, Warnings(invented));
        Assert.NotNull(inventedStore.PeekReviewJson());

        var acceptedStore = new RoomPatternStore();
        var accepted = await StoredTool(acceptedStore).InvokeAsync(Json(Body("""
            "rules":[{"role":"switch","anchor":"entrance","offset_m":0.15}],"anchors":[{"label":"door","page_x":0.4,"page_y":0.1}]
            """)), CancellationToken.None);
        Assert.Equal(true, accepted["stored"]);
        Assert.Equal(SemanticReport.Complete, accepted["semantic_status"]);
        Assert.Equal(0, accepted["semantic_warning_count"]);
    }

    [Fact]
    public async Task One_paired_rule_with_two_observed_receptacles_is_accepted()
    {
        var result = await StoredTool(new RoomPatternStore()).InvokeAsync(Json(Visual(
            Receptacle + "," + ReceptacleRight,
            """
            "rules":[{"role":"receptacle","anchor":"paired_symmetric","offset_m":0.6,"group":"bedside"}]
            """)), CancellationToken.None);
        Assert.Equal(true, result["stored"]);
        Assert.Equal(SemanticReport.Complete, result["semantic_status"]);
        Assert.Equal(0, result["semantic_warning_count"]);
    }

    [Fact]
    public async Task One_pair_rule_can_coexist_with_other_receptacles_of_the_same_role()
    {
        var result = await StoredTool(new RoomPatternStore()).InvokeAsync(Json(Visual(
            Receptacle + "," + ReceptacleRight + "," + ReceptacleExtra,
            """
            "rules":[{"role":"receptacle","anchor":"paired_symmetric","offset_m":0.6,"group":"bedside"}]
            """)), CancellationToken.None);
        Assert.Equal(true, result["stored"]);
        Assert.Equal(SemanticReport.Complete, result["semantic_status"]);
        Assert.Equal(3, result["device_count"]);
    }

    [Fact]
    public async Task Entrance_centre_pair_and_opposite_wall_can_be_proposed_together()
    {
        var result = await StoredTool(new RoomPatternStore()).InvokeAsync(Json(Visual(
            Switch + "," + Luminaire + "," + Data + "," + Receptacle + "," + ReceptacleRight,
            """
            "rules":[
              {"role":"switch","anchor":"entrance","offset_m":0.15},
              {"role":"luminaire","anchor":"room_center"},
              {"role":"receptacle","anchor":"paired_symmetric","offset_m":0.6,"group":"bedside"},
              {"role":"data","anchor":"opposite_boundary"}
            ]
            """)), CancellationToken.None);
        Assert.Equal(true, result["stored"]);
        Assert.Equal(SemanticReport.Complete, result["semantic_status"]);
        var text = Understood(result);
        Assert.Contains("1 switch associated with the entrance", text, StringComparison.Ordinal);
        Assert.Contains("1 ceiling luminaire near room centre", text, StringComparison.Ordinal);
        Assert.Contains("2 receptacles forming a symmetric pair", text, StringComparison.Ordinal);
        Assert.Contains("1 data/TV device on an opposite wall", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task One_malformed_rule_does_not_discard_valid_visual_observations()
    {
        var store = new RoomPatternStore();
        var result = await StoredTool(store).InvokeAsync(Json(Body("""
            "rules":[{"role":"switch","anchor":"entrance","offset_m":0.15},{"role":"switch","anchor":"entrance","x":1.2,"y":3.4}]
            """)), CancellationToken.None);
        Assert.Equal(true, result["stored"]);
        Assert.Equal(SemanticReport.Partial, result["semantic_status"]);
        Assert.Contains(SemanticWarningCodes.MalformedRule, Warnings(result));
        Assert.Contains("1 switch associated with the entrance", Understood(result), StringComparison.Ordinal);
        Assert.Contains("Some semantic relationships were not established.", Review(store), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unknown_semantic_anchor_is_dropped_and_not_promoted()
    {
        var store = new RoomPatternStore();
        var result = await StoredTool(store).InvokeAsync(Json(Body("""
            "rules":[{"role":"switch","anchor":"entrance","offset_m":0.15},{"role":"data","anchor":"beside_the_bed"}]
            """)), CancellationToken.None);
        Assert.Equal(true, result["stored"]);
        Assert.Equal(SemanticReport.Partial, result["semantic_status"]);
        Assert.Contains(SemanticWarningCodes.UnknownAnchor, Warnings(result));
        var review = Review(store);
        Assert.DoesNotContain("beside_the_bed", review, StringComparison.Ordinal);
        Assert.Contains("1 switch associated with the entrance", review, StringComparison.Ordinal);
    }

    [Fact]
    public async Task All_invalid_semantic_rules_keep_the_visual_pattern()
    {
        var store = new RoomPatternStore();
        var result = await StoredTool(store).InvokeAsync(Json(Body("""
            "rules":[{"role":"switch","anchor":"not_a_real_anchor"},{"role":"nope","anchor":"entrance"}]
            """)), CancellationToken.None);
        Assert.Equal(true, result["stored"]);
        Assert.Equal(SemanticReport.None, result["semantic_status"]);
        Assert.Contains("Semantic relationships were not established.", Review(store), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Partial_semantics_still_open_pattern_review()
    {
        var store = new RoomPatternStore();
        var result = await StoredTool(store).InvokeAsync(Json(Body("""
            "rules":[{"role":"switch","anchor":"entrance","offset_m":0.15},{"role":"luminaire","anchor":"ceiling"}]
            """)), CancellationToken.None);
        Assert.Equal(true, result["stored"]);
        Assert.Equal(SemanticReport.Partial, result["semantic_status"]);
        var review = Review(store);
        Assert.True(RoomPatternReview.TryParse(review, out _));
        Assert.Contains("Some semantic relationships were not established.", review, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Common_group_labels_and_accented_anchor_labels_are_accepted()
    {
        var result = await StoredTool(new RoomPatternStore()).InvokeAsync(Json(Body("""
            "rules":[{"role":"receptacle","anchor":"paired_symmetric","offset_m":0.6,"group":"bedside_pair"}],"anchors":[{"label":"entrée","page_x":0.4,"page_y":0.1}]
            """)), CancellationToken.None);
        Assert.Equal(true, result["stored"]);
        Assert.Equal(SemanticReport.Complete, result["semantic_status"]);
        var text = Understood(result);
        Assert.Contains("bedside_pair", text, StringComparison.Ordinal);
        Assert.Contains("entrée", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_secret_inside_a_semantic_rule_is_not_echoed()
    {
        var store = new RoomPatternStore();
        var result = await StoredTool(store).InvokeAsync(Json(Body("""
            "rules":[{"role":"switch","anchor":"entrance","element_ref":"secret-room-ref-99"}]
            """)), CancellationToken.None);
        Assert.Equal(true, result["stored"]);
        var model = JsonSerializer.Serialize(result);
        var review = Review(store);
        Assert.DoesNotContain("secret-room-ref-99", model, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-room-ref-99", review, StringComparison.Ordinal);
        Assert.DoesNotContain("element_ref", model, StringComparison.Ordinal);
        Assert.DoesNotContain("element_ref", review, StringComparison.Ordinal);

        var rejected = await new RoomPatternProposalTool(new UnusedSession(), "instance", new RoomPatternStore(), static () => null)
            .InvokeAsync(Json(Body("\"element_ref\":\"secret-top\"")), CancellationToken.None);
        Assert.Equal(false, rejected["stored"]);
        Assert.Equal(RoomPatternFailures.Shape, rejected["code"]);
    }

    private const string Switch = """{"role":"switch","page_x":0.4,"page_y":0.12,"placement_kind":"boundary"}""";

    private const string Luminaire = """{"role":"luminaire","page_x":0.4,"page_y":0.25,"placement_kind":"interior"}""";

    private const string Data = """{"role":"data","page_x":0.4,"page_y":0.38,"placement_kind":"boundary"}""";

    private const string Receptacle = """{"role":"receptacle","page_x":0.25,"page_y":0.38,"placement_kind":"boundary"}""";

    private const string ReceptacleRight = """{"role":"receptacle","page_x":0.55,"page_y":0.38,"placement_kind":"boundary"}""";

    private const string ReceptacleExtra = """{"role":"receptacle","page_x":0.15,"page_y":0.2,"placement_kind":"boundary"}""";

    private static string Body(string extra) => Visual(Switch, extra);

    private static string Visual(string devices, string extra)
    {
        return """
            {"page_number":1,"boundary":[{"page_x":0.1,"page_y":0.1},{"page_x":0.7,"page_y":0.1},{"page_x":0.7,"page_y":0.4},{"page_x":0.1,"page_y":0.4}],"entrance":{"page_x":0.4,"page_y":0.1},"devices":[
            """ + devices + "]," + extra + "}";
    }

    private static RoomPatternProposalTool StoredTool(RoomPatternStore store)
    {
        var artifact = new AssistantDocumentArtifact(
            "sketch.png",
            AssistantDocumentKind.Image,
            4,
            null,
            false,
            [new AssistantVisualPage(1, "image/png", [1, 2, 3, 4], 100, 100)],
            false,
            1);
        return new RoomPatternProposalTool(new UnusedSession(), "instance", store, () => artifact);
    }

    private static string Understood(IReadOnlyDictionary<string, object?> result)
    {
        return string.Join(" ", Assert.IsAssignableFrom<IEnumerable<string>>(result["understood"]));
    }

    private static IReadOnlyList<string> Warnings(IReadOnlyDictionary<string, object?> result)
    {
        return Assert.IsAssignableFrom<IReadOnlyList<string>>(result["semantic_warnings"]);
    }

    private static string Review(RoomPatternStore store)
    {
        var review = store.PeekReviewJson();
        Assert.NotNull(review);
        return review;
    }

    private static JsonElement Json(string json) => JsonSerializer.Deserialize<JsonElement>(json);

    private static SourceRoomPattern Pattern(IReadOnlyList<PlanPoint> boundary, PlanPoint? entrance, IReadOnlyList<SourceDevice> devices)
    {
        return new SourceRoomPattern(boundary, entrance, devices);
    }

    private static IReadOnlyList<PlanPoint> StudioOuter()
    {
        return
        [
            new(0, 0), new(2, 0), new(2, 0.4), new(4, 0.4), new(4, 0), new(6, 0),
            new(6, 4), new(4, 4), new(4, 3.6), new(2, 3.6), new(2, 4), new(0, 4),
        ];
    }

    private static IReadOnlyList<PlanPoint> Rectangle(double x0, double y0, double x1, double y1)
    {
        return [new(x0, y0), new(x1, y0), new(x1, y1), new(x0, y1)];
    }

    [Fact]
    public void Linked_room_reader_is_bounded_to_top_level_links()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BimIt.Revit.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        var source = File.ReadAllText(Path.Combine(directory.FullName, "src", "BimIt.Revit.Addin", "Capabilities", "RevitReadSpatialRoomsService.cs"));
        Assert.Equal(1, source.Split("GetLinkDocument(").Length - 1);
        Assert.Contains("\"unavailable\"", source, StringComparison.Ordinal);
        Assert.Contains("OfClass(typeof(RevitLinkInstance))", source, StringComparison.Ordinal);
    }

    private sealed class UnusedSession : IAssistantToolSession
    {
        public Task<AssistantMcpReadiness> InitializeAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new AssistantMcpReadiness(false, 0, [], null));

        public Task<AssistantToolCall> CallAsync(string toolName, IReadOnlyDictionary<string, object?> arguments, CancellationToken cancellationToken) =>
            Task.FromResult(new AssistantToolCall(true, "unused", "unused", null));
    }
}
