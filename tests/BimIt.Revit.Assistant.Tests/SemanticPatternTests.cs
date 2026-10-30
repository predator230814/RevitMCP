using System.Text.Json;
using BimIt.Revit.Assistant;
using BimIt.Revit.Assistant.Ipc;
using BimIt.Revit.Assistant.Patterns;
using BimIt.Revit.Assistant.Providers.GitHubCopilot;
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
                Spec("switch", PatternAnchors.Entrance, offset: 0.15),
                Spec("receptacle", PatternAnchors.SourceCorrespondence, pair: true, group: "pair"),
                Spec("luminaire", PatternAnchors.RoomCenter),
                Spec("data", PatternAnchors.OppositeBoundary, offset: 0.2),
            ],
            [new PatternAnchor("bed", 0.5, 0.5)]);
        var text = string.Join(" ", lines);
        Assert.Contains("1 switch · entrance", text, StringComparison.Ordinal);
        Assert.Contains("2 receptacles · symmetric pair · boundary (pair)", text, StringComparison.Ordinal);
        Assert.Contains("1 luminaire · interior · room centre", text, StringComparison.Ordinal);
        Assert.Contains("1 data device · opposite entrance", text, StringComparison.Ordinal);
        Assert.Contains("Saw a visual anchor: bed", text, StringComparison.Ordinal);
        Assert.DoesNotContain("page_", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Entrance_centre_opposite_and_pair_adapt_to_the_target()
    {
        var rules = new[]
        {
            Spec("switch", PatternAnchors.Entrance, offset: 0.15),
            Spec("luminaire", PatternAnchors.RoomCenter),
            Spec("data", PatternAnchors.OppositeBoundary, offset: 0.2),
            Spec("receptacle", PatternAnchors.OppositeBoundary, pair: true, group: "pair"),
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
        Assert.Equal(PatternAdaptationStatus.Resolved, match.AdaptationStatus);
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
            [Spec("switch", PatternAnchors.Entrance, offset: 0.15)]);
        var room = new CandidateRoom("Bedroom", "9", "L1", 1, 2, Rectangle(0, 0, 2, 4), false, [new CandidateOpening(1, 0, "door")]);
        var match = Assert.Single(RoomShapeMatcher.Match(source, [room]));
        var device = Assert.Single(match.Devices);
        Assert.InRange(device.LocalX, 0.2, 1.8);
        Assert.InRange(device.LocalY, 0, 1);
        Assert.Equal(PatternAdaptationStatus.Resolved, match.AdaptationStatus);
    }

    [Fact]
    public void Different_geometry_can_still_resolve_semantic_rules()
    {
        var source = new SourceRoomPattern(
            Rectangle(0, 0, 4, 2),
            new PlanPoint(2, 0),
            [new SourceDevice("switch", 2, 0.2, "boundary", null), new SourceDevice("luminaire", 2, 1, "interior", null)],
            [
                Spec("switch", PatternAnchors.Entrance, offset: 0.15),
                Spec("luminaire", PatternAnchors.RoomCenter),
            ]);
        var room = new CandidateRoom("Bedroom", "4", "L1", 4, 1, Rectangle(0, 0, 8, 2), false, [new CandidateOpening(1, 0, "door")]);
        var match = Assert.Single(RoomShapeMatcher.Match(source, [room]));
        Assert.Equal("aspect_mismatch", match.Reason);
        Assert.Equal(PatternAdaptationStatus.Resolved, match.AdaptationStatus);
        Assert.Equal(2, match.Devices.Count);
    }

    [Fact]
    public void Missing_entrance_stays_unresolved()
    {
        var source = new SourceRoomPattern(
            Rectangle(0, 0, 4, 2),
            new PlanPoint(2, 0),
            [new SourceDevice("switch", 2, 0.2, "boundary", null)],
            [Spec("switch", PatternAnchors.Entrance, offset: 0.15)]);
        var room = new CandidateRoom("Bedroom", "5", "L1", 2, 1, Rectangle(0, 0, 4, 2), false, []);
        var match = Assert.Single(RoomShapeMatcher.Match(source, [room]));
        Assert.Equal("high", match.Band);
        Assert.Equal(PatternAdaptationStatus.Unresolved, match.AdaptationStatus);
        Assert.Empty(match.Devices);
    }

    [Fact]
    public void A_point_inside_a_hole_does_not_become_a_placement()
    {
        var source = new SourceRoomPattern(
            Rectangle(0, 0, 10, 8),
            null,
            [new SourceDevice("luminaire", 5, 4, "interior", null)],
            [Spec("luminaire", PatternAnchors.RoomCenter)]);
        var room = new CandidateRoom("Bedroom", "6", "L1", 5, 4, Rectangle(0, 0, 10, 8), false, [], Holes: [Rectangle(4, 3, 6, 5)]);
        var match = Assert.Single(RoomShapeMatcher.Match(source, [room]));
        Assert.Equal(PatternAdaptationStatus.Unresolved, match.AdaptationStatus);
        Assert.Empty(match.Devices);
    }

    [Fact]
    public async Task Rules_reject_coordinates_and_accept_the_small_vocabulary()
    {
        var inventedStore = new RoomPatternStore();
        var invented = await StoredTool(inventedStore).InvokeAsync(Json(Body("""
            "rules":[{"role":"switch","placement":"boundary","anchor":"entrance","x":1.2,"y":3.4}]
            """)), CancellationToken.None);
        Assert.Equal(true, invented["stored"]);
        Assert.Equal(SemanticReport.None, invented["semantic_status"]);
        Assert.Contains(SemanticWarningCodes.MalformedRule, Warnings(invented));
        Assert.NotNull(inventedStore.PeekReviewJson());

        var acceptedStore = new RoomPatternStore();
        var accepted = await StoredTool(acceptedStore).InvokeAsync(Json(Body("""
            "rules":[{"role":"switch","placement":"boundary","anchor":"entrance","offset_m":0.15}],"anchors":[{"label":"door","page_x":0.4,"page_y":0.1}]
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
            "rules":[{"role":"receptacle","placement":"boundary","anchor":"source_correspondence","count":2,"arrangement":"symmetric_pair","group":"bedside"}]
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
            "rules":[{"role":"receptacle","placement":"boundary","anchor":"source_correspondence","count":2,"arrangement":"symmetric_pair","group":"bedside"}]
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
              {"role":"switch","placement":"boundary","anchor":"entrance","offset_m":0.15},
              {"role":"luminaire","placement":"interior","anchor":"room_center"},
              {"role":"receptacle","placement":"boundary","anchor":"source_correspondence","count":2,"arrangement":"symmetric_pair","group":"bedside"},
              {"role":"data","placement":"boundary","anchor":"opposite_boundary"}
            ]
            """)), CancellationToken.None);
        Assert.Equal(true, result["stored"]);
        Assert.Equal(SemanticReport.Complete, result["semantic_status"]);
        var text = Understood(result);
        Assert.Contains("1 switch · entrance", text, StringComparison.Ordinal);
        Assert.Contains("1 luminaire · interior · room centre", text, StringComparison.Ordinal);
        Assert.Contains("2 receptacles · symmetric pair · boundary", text, StringComparison.Ordinal);
        Assert.Contains("1 data device · opposite entrance", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task One_malformed_rule_does_not_discard_valid_visual_observations()
    {
        var store = new RoomPatternStore();
        var result = await StoredTool(store).InvokeAsync(Json(Body("""
            "rules":[{"role":"switch","placement":"boundary","anchor":"entrance","offset_m":0.15},{"role":"switch","placement":"boundary","anchor":"entrance","x":1.2,"y":3.4}]
            """)), CancellationToken.None);
        Assert.Equal(true, result["stored"]);
        Assert.Equal(SemanticReport.Partial, result["semantic_status"]);
        Assert.Contains(SemanticWarningCodes.MalformedRule, Warnings(result));
        Assert.Contains("1 switch · entrance", Understood(result), StringComparison.Ordinal);
        Assert.Contains("Some semantic relationships were not established.", Review(store), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unknown_semantic_anchor_is_dropped_and_not_promoted()
    {
        var store = new RoomPatternStore();
        var result = await StoredTool(store).InvokeAsync(Json(Body("""
            "rules":[{"role":"switch","placement":"boundary","anchor":"entrance","offset_m":0.15},{"role":"data","anchor":"beside_the_bed"}]
            """)), CancellationToken.None);
        Assert.Equal(true, result["stored"]);
        Assert.Equal(SemanticReport.Partial, result["semantic_status"]);
        Assert.Contains(SemanticWarningCodes.UnknownAnchor, Warnings(result));
        var review = Review(store);
        Assert.DoesNotContain("beside_the_bed", review, StringComparison.Ordinal);
        Assert.Contains("1 switch · entrance", review, StringComparison.Ordinal);
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
            "rules":[{"role":"switch","placement":"boundary","anchor":"entrance","offset_m":0.15},{"role":"luminaire","anchor":"ceiling"}]
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
            "rules":[{"role":"receptacle","placement":"boundary","anchor":"source_correspondence","count":2,"arrangement":"symmetric_pair","group":"bedside_pair"}],"anchors":[{"label":"entrée","page_x":0.4,"page_y":0.1}]
            """)), CancellationToken.None);
        Assert.Equal(true, result["stored"]);
        Assert.Equal(SemanticReport.Complete, result["semantic_status"]);
        var text = Understood(result);
        Assert.Contains("bedside_pair", text, StringComparison.Ordinal);
        Assert.Contains("entrée", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task V2_contract_rejects_each_invalid_item_and_keeps_the_valid_ones()
    {
        var store = new RoomPatternStore();
        var result = await StoredTool(store).InvokeAsync(Json(Visual(
            Switch + "," + Luminaire + "," + Receptacle + "," + ReceptacleRight,
            """
            "rules":[
              {"role":"switch","placement":"boundary","anchor":"entrance","count":1,"arrangement":"single"},
              {"role":"luminaire","placement":"boundary","anchor":"room_center"},
              {"role":"switch","placement":"interior","anchor":"entrance"},
              {"role":"receptacle","placement":"boundary","anchor":"source_correspondence","count":5},
              {"role":"receptacle","placement":"boundary","anchor":"source_correspondence","count":2,"arrangement":"single"},
              {"role":"receptacle","placement":"boundary","anchor":"source_correspondence","count":2,"arrangement":"zigzag"},
              {"role":"receptacle","placement":"boundary","anchor":"source_correspondence","count":3,"arrangement":"distributed"},
              {"role":"receptacle","placement":"boundary","anchor":"paired_symmetric","count":2}
            ]
            """)), CancellationToken.None);
        Assert.Equal(true, result["stored"]);
        Assert.Equal(SemanticReport.Partial, result["semantic_status"]);
        var warnings = Warnings(result);
        Assert.Contains(SemanticWarningCodes.IncompatiblePlacement, warnings);
        Assert.Contains(SemanticWarningCodes.InvalidCount, warnings);
        Assert.Contains(SemanticWarningCodes.UnknownArrangement, warnings);
        Assert.Contains(SemanticWarningCodes.UnsupportedArrangement, warnings);
        Assert.Contains(SemanticWarningCodes.UnknownAnchor, warnings);
        var review = Review(store);
        Assert.Contains("1 switch · entrance", review, StringComparison.Ordinal);
        Assert.DoesNotContain("paired_symmetric", review, StringComparison.Ordinal);
        Assert.DoesNotContain("zigzag", review, StringComparison.Ordinal);
    }

    [Fact]
    public async Task V2_items_require_a_placement_and_default_the_arrangement_from_the_count()
    {
        var missing = await StoredTool(new RoomPatternStore()).InvokeAsync(Json(Body("""
            "rules":[{"role":"switch","anchor":"entrance"}]
            """)), CancellationToken.None);
        Assert.Equal(true, missing["stored"]);
        Assert.Contains(SemanticWarningCodes.MalformedRule, Warnings(missing));

        var store = new RoomPatternStore();
        var paired = await StoredTool(store).InvokeAsync(Json(Visual(
            Luminaire + "," + """{"role":"luminaire","page_x":0.55,"page_y":0.25,"placement_kind":"interior"}""",
            """
            "rules":[{"role":"luminaire","placement":"interior","anchor":"room_center","count":2}]
            """)), CancellationToken.None);
        Assert.Equal(SemanticReport.Complete, paired["semantic_status"]);
        var text = Understood(paired);
        Assert.Contains("2 luminaires · symmetric pair · interior", text, StringComparison.Ordinal);
        Assert.DoesNotContain("receptacle", text, StringComparison.Ordinal);
        Assert.DoesNotContain("wall in sketch", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Cardinality_is_independent_of_the_observed_device_count()
    {
        var devices = new[]
        {
            new SourceDevice("receptacle", 0.2, 0.9, "boundary", null),
            new SourceDevice("receptacle", 0.4, 0.9, "boundary", null),
            new SourceDevice("receptacle", 0.6, 0.9, "boundary", null),
            new SourceDevice("receptacle", 0.95, 0.5, "boundary", null),
        };
        var specs = PatternEvidence.AssignDevices(
            [
                Spec("receptacle", PatternAnchors.SourceCorrespondence, pair: true),
                Spec("receptacle", PatternAnchors.SourceCorrespondence, devices: [3]),
            ],
            devices);
        Assert.Equal(2, specs[0].Count);
        Assert.Equal(2, specs[0].DeviceIndexes!.Count);
        Assert.DoesNotContain(3, specs[0].DeviceIndexes!);
        Assert.Equal([3], specs[1].DeviceIndexes);
    }

    [Fact]
    public void Source_evidence_distinguishes_pairs_on_different_walls_without_identities()
    {
        var devices = new[]
        {
            new SourceDevice("receptacle", 3, 5.9, "boundary", null),
            new SourceDevice("receptacle", 5, 5.9, "boundary", null),
            new SourceDevice("receptacle", 9.9, 2, "boundary", null),
            new SourceDevice("receptacle", 9.9, 4, "boundary", null),
            new SourceDevice("luminaire", 4, 3, "interior", null),
            new SourceDevice("luminaire", 6, 3, "interior", null),
        };
        var specs = PatternEvidence.AssignDevices(
            [
                Spec("receptacle", PatternAnchors.SourceCorrespondence, pair: true, devices: [0, 1]),
                Spec("receptacle", PatternAnchors.SourceCorrespondence, pair: true, devices: [2, 3]),
                Spec("luminaire", PatternAnchors.RoomCenter, pair: true),
            ],
            devices);
        var evidence = PatternEvidence.Describe(new SourceRoomPattern(Rectangle(0, 0, 10, 6), null, devices, specs));
        Assert.Equal("top", evidence[0].Side);
        Assert.Equal("right", evidence[1].Side);
        Assert.NotEqual(evidence[0].EdgeIndex, evidence[1].EdgeIndex);
        Assert.Equal(PatternPlacements.Boundary, evidence[0].Placement);
        Assert.Equal(PatternPlacements.Interior, evidence[2].Placement);
        Assert.Equal([4, 5], evidence[2].DeviceIndexes);
        Assert.InRange(evidence[0].Centroid!.Value.X, 0.35, 0.45);
        Assert.InRange(evidence[0].Along!.Value, 0, 1);
        Assert.NotNull(evidence[0].Spread);
        var json = JsonSerializer.Serialize(evidence);
        Assert.DoesNotContain("ref", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Id\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_secret_inside_a_semantic_rule_is_not_echoed()
    {
        var store = new RoomPatternStore();
        var result = await StoredTool(store).InvokeAsync(Json(Body("""
            "rules":[{"role":"switch","placement":"boundary","anchor":"entrance","element_ref":"secret-room-ref-99"}]
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

    private static string WithoutEntrance(string devices, string extra)
    {
        return """
            {"page_number":1,"boundary":[{"page_x":0.1,"page_y":0.1},{"page_x":0.7,"page_y":0.1},{"page_x":0.7,"page_y":0.4},{"page_x":0.1,"page_y":0.4}],"devices":[
            """ + devices + "]," + extra + "}";
    }

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
        using var document = JsonDocument.Parse(review);
        return JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
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

    private static SourceRoomPattern PlainStudio()
    {
        return new SourceRoomPattern(Rectangle(0, 0, 10, 6), new PlanPoint(1, 0), [new SourceDevice("switch", 1, 0.2, "boundary", null)]);
    }

    private static CandidateRoom Candidate(IReadOnlyList<PlanPoint> loop, IReadOnlyList<IReadOnlyList<PlanPoint>>? holes = null)
    {
        return new CandidateRoom("Studio", "1", "L1", loop.Average(point => point.X), loop.Average(point => point.Y), loop, false, [], Holes: holes);
    }

    private static List<PlanPoint> SegmentedRectangle(double x0, double y0, double x1, double y1, int segments)
    {
        var points = new List<PlanPoint>();
        void Edge(double ax, double ay, double bx, double by)
        {
            for (var step = 0; step < segments; step++)
            {
                var t = step / (double)segments;
                points.Add(new PlanPoint(ax + ((bx - ax) * t), ay + ((by - ay) * t)));
            }
        }

        Edge(x0, y0, x1, y0);
        Edge(x1, y0, x1, y1);
        Edge(x1, y1, x0, y1);
        Edge(x0, y1, x0, y0);
        return points;
    }

    private static List<PlanPoint> Comb()
    {
        var points = new List<PlanPoint> { new(0, 0) };
        for (var tooth = 0; tooth < 4; tooth++)
        {
            var x = tooth * 2;
            points.Add(new PlanPoint(x + 1, 0));
            points.Add(new PlanPoint(x + 1, 1));
            points.Add(new PlanPoint(x + 2, 1));
            points.Add(new PlanPoint(x + 2, 0));
        }

        points.Add(new PlanPoint(8, 3));
        points.Add(new PlanPoint(0, 3));
        return points;
    }

    private static IReadOnlyList<PlanPoint> Rectangle(double x0, double y0, double x1, double y1)
    {
        return [new(x0, y0), new(x1, y0), new(x1, y1), new(x0, y1)];
    }

    [Fact]
    public void Over_segmented_orthogonal_room_stays_matchable()
    {
        var dense = SegmentedRectangle(0, 0, 10, 6, 6);
        Assert.True(dense.Count > RoomShapeMatcher.MaxStraightVertices);
        var match = Assert.Single(RoomShapeMatcher.Match(PlainStudio(), [Candidate(dense)]));
        Assert.NotEqual("boundary_too_complex", match.Reason);
        Assert.NotEqual("rejected", match.Band);
    }

    [Fact]
    public void Many_meaningful_corners_stay_boundary_too_complex()
    {
        var comb = Comb();
        Assert.True(comb.Count > RoomShapeMatcher.MaxStraightVertices);
        var match = Assert.Single(RoomShapeMatcher.Match(PlainStudio(), [Candidate(comb)]));
        Assert.Equal("boundary_too_complex", match.Reason);
        Assert.Equal("rejected", match.Band);
    }

    [Fact]
    public void A_dense_hole_is_not_counted_as_outer_loop_complexity()
    {
        var hole = SegmentedRectangle(3, 2, 5, 4, 6);
        Assert.True(hole.Count > RoomShapeMatcher.MaxStraightVertices);
        var room = Candidate(SegmentedRectangle(0, 0, 10, 6, 6), [hole]);
        var match = Assert.Single(RoomShapeMatcher.Match(PlainStudio(), [room]));
        Assert.NotEqual("boundary_too_complex", match.Reason);
        Assert.NotEqual("rejected", match.Band);
    }

    [Fact]
    public void Self_intersection_and_non_orthogonal_checks_remain()
    {
        var bowtie = Candidate([new(0, 0), new(5, 0), new(5, 2), new(1, 2), new(1, 1), new(4, 1), new(4, 3), new(0, 3)]);
        var diamond = Candidate([new(0, 1), new(1, 0), new(0, -1), new(-1, 0)]);
        var matches = RoomShapeMatcher.Match(PlainStudio(), [bowtie, diamond]);
        Assert.Equal("self_intersecting_boundary", matches[0].Reason);
        Assert.Equal("non_orthogonal_boundary", matches[1].Reason);
    }

    [Fact]
    public void Prompt_documents_the_six_natural_anchor_mappings_without_adding_a_tool()
    {
        var guidance = GitHubCopilotSessionBlueprint.SemanticRelationshipGuidance;
        Assert.Contains(guidance, GitHubCopilotSessionBlueprint.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains(guidance, GitHubCopilotSessionBlueprint.RoomPatternToolDescription, StringComparison.Ordinal);
        Assert.Contains("explicitly requested by the user", guidance, StringComparison.Ordinal);
        Assert.Contains("Never invent a relationship unsupported by either the image or the user.", guidance, StringComparison.Ordinal);
        Assert.Contains("The user's explicit scope overrides the drawing", guidance, StringComparison.Ordinal);
        Assert.Contains("does not require a source-page entrance point", guidance, StringComparison.Ordinal);
        Assert.Contains("A pair is an arrangement, not an anchor.", guidance, StringComparison.Ordinal);
        Assert.Contains("two receptacles drawn on one wall → receptacle, boundary, source_correspondence, count 2, symmetric_pair", guidance, StringComparison.Ordinal);
        Assert.Contains("two ceiling luminaires → luminaire, interior, room_center, count 2, symmetric_pair", guidance, StringComparison.Ordinal);
        Assert.Contains("user says switch at entrance → switch, boundary, entrance, count 1, single", guidance, StringComparison.Ordinal);
        Assert.Contains("user says data on wall opposite entrance → data, boundary, opposite_boundary, count 1, single", guidance, StringComparison.Ordinal);
        Assert.Contains("Do not call every repeated symbol a wall pair.", guidance, StringComparison.Ordinal);
        Assert.DoesNotContain("paired_symmetric", GitHubCopilotSessionBlueprint.SystemPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("paired_symmetric", GitHubCopilotSessionBlueprint.RoomPatternToolDescription, StringComparison.Ordinal);
        Assert.DoesNotContain("Describe only what you see", GitHubCopilotSessionBlueprint.SystemPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("element_ref", GitHubCopilotSessionBlueprint.RoomPatternToolDescription, StringComparison.Ordinal);
        Assert.Equal(3, AssistantModelTools.LocalNames.Length);
    }

    [Fact]
    public async Task Explicit_user_rules_store_without_a_source_entrance()
    {
        var store = new RoomPatternStore();
        var result = await StoredTool(store).InvokeAsync(Json(WithoutEntrance(
            Switch + "," + Luminaire + "," + Data + "," + Receptacle + "," + ReceptacleRight,
            """
            "rules":[
              {"role":"switch","placement":"boundary","anchor":"entrance","basis":"user_stated"},
              {"role":"luminaire","placement":"interior","anchor":"room_center","basis":"user_stated"},
              {"role":"receptacle","placement":"boundary","anchor":"source_correspondence","count":2,"arrangement":"symmetric_pair","group":"bedside","basis":"user_stated"},
              {"role":"data","placement":"boundary","anchor":"opposite_boundary","basis":"user_stated"},
              {"role":"receptacle","placement":"boundary","anchor":"nearest_boundary","basis":"not_a_basis"}
            ]
            """)), CancellationToken.None);
        Assert.Equal(true, result["stored"]);
        Assert.Equal(SemanticReport.Complete, result["semantic_status"]);
        var review = Review(store);
        Assert.Contains("User requested:", review, StringComparison.Ordinal);
        Assert.Contains("1 switch · entrance", review, StringComparison.Ordinal);
        Assert.Contains("1 luminaire · interior · room centre", review, StringComparison.Ordinal);
        Assert.Contains("2 receptacles · symmetric pair · boundary", review, StringComparison.Ordinal);
        Assert.Contains("1 data device · opposite entrance", review, StringComparison.Ordinal);
        Assert.Contains("Observed:", review, StringComparison.Ordinal);
        Assert.Contains("1 receptacle · nearest wall", review, StringComparison.Ordinal);
        Assert.DoesNotContain("not_a_basis", review, StringComparison.Ordinal);
        Assert.DoesNotContain("element_ref", review, StringComparison.Ordinal);
        Assert.DoesNotContain("element_ref", JsonSerializer.Serialize(result), StringComparison.Ordinal);
    }

    [Fact]
    public void User_stated_centre_resolves_and_entrance_rules_fail_without_a_door()
    {
        var rules = new[]
        {
            Spec("switch", PatternAnchors.Entrance, offset: 0.15, basis: PatternBasis.UserStated),
            Spec("luminaire", PatternAnchors.RoomCenter, basis: PatternBasis.UserStated),
            Spec("receptacle", PatternAnchors.OppositeBoundary, pair: true, group: "bedside", basis: PatternBasis.UserStated),
            Spec("data", PatternAnchors.OppositeBoundary, offset: 0.2, basis: PatternBasis.UserStated),
        };
        var source = new SourceRoomPattern(
            Rectangle(0, 0, 1, 1),
            null,
            [
                new SourceDevice("switch", 0.2, 0.1, "boundary", null),
                new SourceDevice("luminaire", 0.5, 0.5, "interior", null),
                new SourceDevice("receptacle", 0.3, 0.9, "boundary", null),
                new SourceDevice("receptacle", 0.7, 0.9, "boundary", null),
                new SourceDevice("data", 0.5, 0.9, "boundary", null),
            ],
            rules);
        var noDoor = new CandidateRoom("Studio", "9", "L1", 5, 3, Rectangle(0, 0, 10, 6), false, []);
        var missed = Assert.Single(RoomShapeMatcher.Match(source, [noDoor]));
        Assert.Equal(PatternAdaptationStatus.Partial, missed.AdaptationStatus);
        Assert.Equal("luminaire", Assert.Single(missed.Devices).Role);

        var oneDoor = new CandidateRoom("Studio", "203", "L2", 5, 3, Rectangle(0, 0, 10, 6), false, [new CandidateOpening(1, 0, "door")]);
        var placed = Assert.Single(RoomShapeMatcher.Match(source, [oneDoor]));
        Assert.Equal(PatternAdaptationStatus.Resolved, placed.AdaptationStatus);
        var byRole = placed.Devices.GroupBy(device => device.Role).ToDictionary(group => group.Key, group => group.ToArray());
        Assert.InRange(byRole["switch"][0].LocalY, 0, 1.5);
        Assert.InRange(byRole["luminaire"][0].LocalX, 4.5, 5.5);
        Assert.InRange(byRole["luminaire"][0].LocalY, 2.5, 3.5);
        Assert.Equal(2, byRole["receptacle"].Length);
        Assert.True(byRole["data"][0].LocalY > 4);
    }

    [Fact]
    public void Several_target_doors_without_a_source_entrance_stay_unresolved()
    {
        var source = new SourceRoomPattern(
            Rectangle(0, 0, 1, 1),
            null,
            [new SourceDevice("switch", 0.2, 0.1, "boundary", null)],
            [Spec("switch", PatternAnchors.Entrance, offset: 0.15, basis: PatternBasis.UserStated)]);
        var room = new CandidateRoom(
            "Studio",
            "203",
            "L2",
            5,
            3,
            Rectangle(0, 0, 10, 6),
            false,
            [new CandidateOpening(1, 0, "door"), new CandidateOpening(9, 0, "door")]);
        var match = Assert.Single(RoomShapeMatcher.Match(source, [room]));
        Assert.Equal(PatternAdaptationStatus.Unresolved, match.AdaptationStatus);
    }

    internal static PatternSpec Spec(
        string role,
        string anchor,
        bool pair = false,
        double offset = 0,
        string? group = null,
        string? basis = null,
        IReadOnlyList<int>? devices = null)
    {
        return new PatternSpec(
            role,
            PatternAnchors.PlacementFor(anchor),
            anchor,
            pair ? 2 : 1,
            pair ? PatternArrangements.SymmetricPair : PatternArrangements.Single,
            offset,
            group,
            null,
            basis,
            devices);
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
