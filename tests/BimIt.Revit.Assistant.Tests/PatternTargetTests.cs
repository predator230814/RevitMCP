using BimIt.Revit.Assistant.Patterns;
using BimIt.Revit.Assistant.Providers.GitHubCopilot;
using BimIt.Revit.Assistant.Ipc;
using Xunit;

namespace BimIt.Revit.Assistant.Tests;

public sealed class PatternTargetTests
{
    [Fact]
    public async Task Corridor_door_is_chosen_before_deterministic_placement()
    {
        var steps = new List<string> { "source", "discovery" };
        var (store, pending) = Store(Studio(), "sr1.l.hidden-link-room");
        Assert.DoesNotContain("unresolved_rule", store.PeekReviewJson() ?? "", StringComparison.Ordinal);
        var reasoner = new FakeReasoner
        {
            Steps = steps,
            Next = new PatternTargetReasoningResult(false,
            [
                new PatternTargetRoomChoice("room_0", "compatible", "door_0",
                [
                    new PatternTargetEdgeBinding("rule_2", "edge_2"),
                ]),
            ]),
        };
        await PatternTargetOrchestrator.CompleteAsync(store, reasoner, CancellationToken.None);
        steps.Add("review");
        Assert.Equal(["source", "discovery", "reasoning", "review"], steps);
        var review = store.PeekReviewJson() ?? "";
        Assert.Contains("Entrance: door connected to Corridor", review, StringComparison.Ordinal);
        Assert.Contains("Resolved: switch, luminaire, receptacle pair, data", review, StringComparison.Ordinal);
        Assert.DoesNotContain("door_0", review, StringComparison.Ordinal);
        Assert.DoesNotContain("sr1", review, StringComparison.Ordinal);
        Assert.DoesNotContain("hidden-link-room", review, StringComparison.Ordinal);
        var placed = store.CurrentGeneration!.Rooms[0];
        Assert.NotEqual("rejected", placed.Band);
        var light = placed.Devices.Single(device => device.Role == "luminaire");
        Assert.InRange(light.XMetres, 4.5, 5.5);
        Assert.InRange(light.YMetres, 2.5, 3.5);
        var entrance = placed.Devices.Single(device => device.Role == "switch");
        Assert.InRange(entrance.XMetres, 0, 2.5);
        Assert.InRange(entrance.YMetres, 0, 1.5);
        Assert.Equal(2, placed.Devices.Count(device => device.Role == "receptacle"));
        Assert.True(placed.Devices.Where(device => device.Role == "receptacle").All(device => device.YMetres > 4));
        Assert.True(placed.Devices.Single(device => device.Role == "data").YMetres > 4);
        Assert.Contains("Corridor", reasoner.Seen!.Rooms[0].Doors[0].OtherSideName, StringComparison.Ordinal);
        Assert.Equal("Bathroom", reasoner.Seen.Rooms[0].Doors[1].OtherSideName);
    }

    [Fact]
    public async Task Invented_keys_and_uncertain_assessments_stay_unresolved()
    {
        var (inventedStore, _) = Store(Studio(), "room-ref");
        await PatternTargetOrchestrator.CompleteAsync(inventedStore, new FakeReasoner
        {
            Next = new PatternTargetReasoningResult(false,
            [
                new PatternTargetRoomChoice("room_0", "compatible", "door_9", []),
            ]),
        }, CancellationToken.None);
        var invented = inventedStore.PeekReviewJson() ?? "";
        Assert.Contains("unresolved_rule", invented, StringComparison.Ordinal);
        Assert.Contains("Two possible entrance doors remain plausible", invented, StringComparison.Ordinal);
        Assert.DoesNotContain("Resolved: switch", invented, StringComparison.Ordinal);

        var (uncertainStore, _) = Store(Studio(), "room-ref");
        await PatternTargetOrchestrator.CompleteAsync(uncertainStore, new FakeReasoner
        {
            Next = new PatternTargetReasoningResult(false,
            [
                new PatternTargetRoomChoice("room_0", "uncertain", null, []),
            ]),
        }, CancellationToken.None);
        Assert.Contains("Unresolved: switch", uncertainStore.PeekReviewJson() ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unavailable_reasoner_keeps_the_geometric_review()
    {
        var (store, _) = Store(Studio(), "room-ref");
        var before = store.PeekReviewJson();
        await PatternTargetOrchestrator.CompleteAsync(store, new FakeReasoner { Next = PatternTargetReasoningResult.None }, CancellationToken.None);
        var after = store.PeekReviewJson() ?? "";
        Assert.Contains("target_reasoning_unavailable", after, StringComparison.Ordinal);
        Assert.DoesNotContain("unresolved_rule", after, StringComparison.Ordinal);
        Assert.Contains("User requested:", before ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public void Snake_case_reply_is_read_and_extra_fields_are_refused()
    {
        var reply = "Here is the result:\n```json\n{\"rooms\":[{\"room_key\":\"room_0\",\"assessment\":\"compatible\",\"entrance_feature_key\":\"door_0\",\"bindings\":[{\"rule_key\":\"rule_2\",\"target_edge_key\":\"edge_2\"}]}]}\n```";
        Assert.True(PatternTargetResponseParser.TryParse(reply, out var result));
        Assert.False(result.Unavailable);
        var room = Assert.Single(result.Rooms);
        Assert.Equal("room_0", room.RoomKey);
        Assert.Equal("door_0", room.EntranceFeatureKey);
        Assert.Equal("edge_2", Assert.Single(room.Bindings).EdgeKey);

        Assert.True(PatternTargetResponseParser.TryParse("{\"rooms\":[{\"room_key\":\"room_0\",\"assessment\":\"uncertain\",\"entrance_feature_key\":null}]}", out var uncertain));
        Assert.Null(Assert.Single(uncertain.Rooms).EntranceFeatureKey);

        Assert.False(PatternTargetResponseParser.TryParse("{\"rooms\":[{\"room_key\":\"room_0\",\"assessment\":\"compatible\",\"x\":1.2,\"y\":3.4}]}", out _));
        Assert.False(PatternTargetResponseParser.TryParse("{\"rooms\":[{\"room_key\":\"room_0\",\"assessment\":\"approved\"}]}", out _));
        Assert.False(PatternTargetResponseParser.TryParse("no json here", out _));
    }

    [Fact]
    public void Composed_prompt_states_the_reply_shape_and_carries_no_identity()
    {
        var rooms = new[] { Studio() };
        var matches = RoomShapeMatcher.Match(Pattern(), rooms, resolveSemantics: false);
        var draft = PatternTargetContext.Build(Pattern(), rooms, matches, Review(), namedRooms: true);
        var prompt = PatternTargetContext.Compose(draft!.Request);
        Assert.Contains("\"room_key\"", prompt, StringComparison.Ordinal);
        Assert.Contains("entrance_feature_key", prompt, StringComparison.Ordinal);
        Assert.Contains("\"other_side_name\":\"Corridor\"", prompt, StringComparison.Ordinal);
        Assert.Contains("\"other_side_name\":\"Bathroom\"", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("ref-0", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unreadable_reply_names_the_failure()
    {
        var (store, _) = Store(Studio(), "room-ref");
        await PatternTargetOrchestrator.CompleteAsync(store, new FakeReasoner
        {
            Next = PatternTargetReasoningResult.Failed(PatternTargetReasoningResult.UnreadableResponse),
        }, CancellationToken.None);
        Assert.Contains("target_reasoning_unavailable: unreadable_response", store.PeekReviewJson() ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public void Stale_generation_result_is_ignored()
    {
        var (store, pending) = Store(Studio(), "room-ref");
        var before = store.PeekReviewJson();
        store.CompleteTargetReasoning("ffffffffffffffffffffffffffffffff", PatternTargetResolution.Apply(pending, PatternTargetReasoningResult.None));
        Assert.Equal(before, store.PeekReviewJson());
    }

    [Fact]
    public void Target_context_is_bounded_normalized_and_identity_free()
    {
        var rooms = Enumerable.Range(0, 10).Select(index => Room(index, "Studio " + index.ToString())).ToArray();
        var matches = RoomShapeMatcher.Match(Pattern(), rooms, resolveSemantics: false);
        var draft = PatternTargetContext.Build(Pattern(), rooms, matches, Review(), namedRooms: true);
        Assert.NotNull(draft);
        Assert.Equal(8, draft!.Request.Rooms.Count);
        var json = PatternTargetContext.Serialize(draft.Request);
        Assert.DoesNotContain("spatial_room_ref", json, StringComparison.Ordinal);
        Assert.DoesNotContain("UniqueId", json, StringComparison.Ordinal);
        Assert.DoesNotContain("element_ref", json, StringComparison.Ordinal);
        Assert.DoesNotContain("document_id", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\\", json, StringComparison.Ordinal);
        Assert.DoesNotContain(".rvt", json, StringComparison.OrdinalIgnoreCase);
        Assert.All(draft.Request.Rooms.SelectMany(room => room.Doors), door =>
        {
            Assert.InRange(door.X, 0, 1);
            Assert.InRange(door.Y, 0, 1);
        });
        Assert.All(draft.Request.Rules, rule => Assert.StartsWith("rule_", rule.RuleKey, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Different_and_rotated_rooms_place_the_switch_on_their_own_entrance()
    {
        var wide = Room(0, "Studio 203");
        wide = wide with { Loop = Rectangle(0, 0, 16, 6), CentroidX = 8, CentroidY = 3, Openings = [Door(2, 0, "Corridor", "1"), Door(14, 0, "Bathroom", "2")] };
        var (wideStore, _) = Store(wide, "wide-ref");
        await PatternTargetOrchestrator.CompleteAsync(wideStore, Choice("door_0"), CancellationToken.None);
        var wideSwitch = wideStore.CurrentGeneration!.Rooms[0].Devices.Single(device => device.Role == "switch");

        var tall = Room(0, "Studio 406");
        tall = tall with
        {
            Loop = Rectangle(0, 0, 6, 10),
            CentroidX = 3,
            CentroidY = 5,
            Openings = [Door(3, 0, "Corridor", "1")],
        };
        var (tallStore, _) = Store(tall, "tall-ref");
        await PatternTargetOrchestrator.CompleteAsync(tallStore, Choice("door_0"), CancellationToken.None);
        var tallSwitch = tallStore.CurrentGeneration!.Rooms[0].Devices.Single(device => device.Role == "switch");
        Assert.NotEqual(Math.Round(wideSwitch.XMetres, 2), Math.Round(tallSwitch.XMetres, 2));
        Assert.InRange(tallSwitch.YMetres, 0, 1.5);
    }

    [Fact]
    public async Task L_shaped_studio_with_wall_jogs_places_pair_and_data_on_the_wall_facing_the_entrance()
    {
        var room = Room(0, "Two Story Studio Unit 406") with
        {
            Loop =
            [
                new PlanPoint(0, 0), new PlanPoint(4, 0), new PlanPoint(4, 5), new PlanPoint(9, 5),
                new PlanPoint(9, 9.9), new PlanPoint(8.9, 9.9), new PlanPoint(8.9, 10), new PlanPoint(0, 10),
                new PlanPoint(0, 3.3), new PlanPoint(0.1, 3.3), new PlanPoint(0.1, 3), new PlanPoint(0, 3),
            ],
            CentroidX = 3.73,
            CentroidY = 5.96,
            Openings = [Door(1, 0, "Corridor", "C4"), Door(4, 2, "Bathroom", "B4")],
        };
        var (store, _) = Store(room, "l-ref");
        await PatternTargetOrchestrator.CompleteAsync(store, Choice("door_0"), CancellationToken.None);
        var review = store.PeekReviewJson() ?? "";
        Assert.Contains("Entrance: door connected to Corridor", review, StringComparison.Ordinal);
        Assert.Contains("Resolved: switch, luminaire, receptacle pair, data", review, StringComparison.Ordinal);
        Assert.DoesNotContain("unresolved_rule", review, StringComparison.Ordinal);
        var devices = store.CurrentGeneration!.Rooms[0].Devices;
        var data = devices.Single(device => device.Role == "data");
        Assert.InRange(data.XMetres, 0.5, 1.5);
        Assert.InRange(data.YMetres, 9.5, 10);
        var pair = devices.Where(device => device.Role == "receptacle").ToArray();
        Assert.Equal(2, pair.Length);
        Assert.All(pair, device =>
        {
            Assert.InRange(device.YMetres, 9.5, 10);
            Assert.InRange(device.XMetres, 0.1, 4);
        });
        Assert.Equal(1.2, Math.Abs(pair[0].XMetres - pair[1].XMetres), 3);
    }

    [Fact]
    public async Task A_hole_over_the_centre_keeps_the_luminaire_out()
    {
        var room = Studio() with { Holes = [Rectangle(4, 2, 6, 4)] };
        var (store, _) = Store(room, "hole-ref");
        await PatternTargetOrchestrator.CompleteAsync(store, Choice("door_0"), CancellationToken.None);
        var review = store.PeekReviewJson() ?? "";
        Assert.Contains("unresolved_rule", review, StringComparison.Ordinal);
        Assert.DoesNotContain("\"role\":\"luminaire\"", review, StringComparison.Ordinal);
    }

    [Fact]
    public void Second_session_exposes_no_model_tools_and_no_mcp()
    {
        var session = GitHubCopilotSessionBlueprint.CreateTargetSessionConfig(Path.GetTempPath(), "auto");
        Assert.NotNull(session.McpServers);
        Assert.Empty(session.McpServers!);
        Assert.NotNull(session.Tools);
        Assert.Empty(session.Tools!);
        Assert.False(session.EnableSkills);
        Assert.False(session.Memory!.Enabled);
        Assert.Equal(7, AssistantModelTools.Names.Length);
        Assert.DoesNotContain(session.Tools, tool => AssistantTools.IsTrustedWriteTool(tool.Name));
        Assert.DoesNotContain("bimit_propose_room_pattern", GitHubCopilotSessionBlueprint.TargetReasoningPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Linked_reader_keeps_one_link_lookup_and_safe_other_side_labels()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BimIt.Revit.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        var source = File.ReadAllText(Path.Combine(directory!.FullName, "src", "BimIt.Revit.Addin", "Capabilities", "RevitReadSpatialRoomsService.cs"));
        Assert.Equal(1, source.Split("GetLinkDocument(").Length - 1);
        Assert.Contains("\"unavailable\"", source, StringComparison.Ordinal);
        Assert.Contains("get_FromRoom", source, StringComparison.Ordinal);
        Assert.Contains("OtherSideStatus", source, StringComparison.Ordinal);
        Assert.Contains("DescribeSide", source, StringComparison.Ordinal);
    }

    private static FakeReasoner Choice(string door)
    {
        return new FakeReasoner
        {
            Next = new PatternTargetReasoningResult(false, [new PatternTargetRoomChoice("room_0", "compatible", door, [])]),
        };
    }

    private static (RoomPatternStore Store, PatternTargetPending Pending) Store(CandidateRoom room, string roomRef)
    {
        room = room with { ElementRef = roomRef };
        var source = Pattern();
        var matches = RoomShapeMatcher.Match(source, [room], resolveSemantics: false);
        var review = Review();
        var draft = PatternTargetContext.Build(source, [room], matches, review, namedRooms: true);
        Assert.NotNull(draft);
        var generationId = "0123456789abcdef0123456789abcdef";
        review.Generation = generationId;
        review.PageNumber = 1;
        var store = new RoomPatternStore();
        var generation = new RoomPatternGeneration
        {
            GenerationId = generationId,
            InstanceId = "11111111-2222-3333-4444-555555555555",
            DocumentId = "document",
            ArtifactId = "artifact",
            PageNumber = 1,
            Source = source,
            Rooms = [],
            Options = new Dictionary<string, LoadedMepTypeOption>(),
        };
        store.Replace(new RoomPatternRecord("artifact", 1, source.Devices.Count), RoomPatternReview.Serialize(review), generation);
        var pending = new PatternTargetPending(generationId, generation.InstanceId, generation.DocumentId, "artifact", 1, draft!.Request, draft.Snapshot);
        store.RememberPending(pending);
        return (store, pending);
    }

    private static SourceRoomPattern Pattern()
    {
        return new SourceRoomPattern(
            Rectangle(0, 0, 10, 6),
            null,
            [
                new SourceDevice("switch", 1, 0.2, "boundary", null),
                new SourceDevice("luminaire", 5, 3, "interior", null),
                new SourceDevice("receptacle", 4, 5.5, "boundary", null),
                new SourceDevice("receptacle", 6, 5.5, "boundary", null),
                new SourceDevice("data", 5, 5.5, "boundary", null),
            ],
            [
                new PatternRule("switch", PatternAnchors.Entrance, 0.15, null, null, PatternRuleBasis.UserStated),
                new PatternRule("luminaire", PatternAnchors.RoomCenter, 0, null, null, PatternRuleBasis.UserStated),
                new PatternRule("receptacle", PatternAnchors.PairedSymmetric, 0.6, "pair", null, PatternRuleBasis.UserStated),
                new PatternRule("data", PatternAnchors.OppositeBoundary, 0.2, null, null, PatternRuleBasis.UserStated),
            ]);
    }

    private static CandidateRoom Studio()
    {
        return Room(0, "Studio Unit 203") with
        {
            Openings =
            [
                Door(1, 0, "Corridor", "C1"),
                Door(9, 0, "Bathroom", "B1"),
            ],
        };
    }

    private static CandidateRoom Room(int index, string name)
    {
        return new CandidateRoom(name, index.ToString(), "L2", 5, 3, Rectangle(0, 0, 10, 6), false, [Door(1, 0, "Corridor", "1")], "ref-" + index.ToString(), null, "link", "Snowdon");
    }

    private static CandidateOpening Door(double x, double y, string other, string number)
    {
        return new CandidateOpening(x, y, "door", other, number, "room");
    }

    private static RoomPatternReview.Review Review()
    {
        return new RoomPatternReview.Review
        {
            PageNumber = 1,
            ReadState = "matched",
            Boundary = [new() { X = 0, Y = 0 }, new() { X = 1, Y = 0 }, new() { X = 1, Y = 1 }, new() { X = 0, Y = 1 }],
            Devices = [new() { Role = "switch", Placement = "interior", X = 0.2, Y = 0.2 }],
            Understood = ["User requested:", "1 switch associated with the entrance"],
            Rooms = [],
        };
    }

    private static List<PlanPoint> Rectangle(double x0, double y0, double x1, double y1)
    {
        return [new PlanPoint(x0, y0), new PlanPoint(x1, y0), new PlanPoint(x1, y1), new PlanPoint(x0, y1)];
    }

    private sealed class FakeReasoner : IPatternTargetReasoner
    {
        public List<string>? Steps { get; init; }

        public PatternTargetReasoningResult Next { get; init; } = PatternTargetReasoningResult.None;

        public PatternTargetReasoningRequest? Seen { get; private set; }

        public Task<PatternTargetReasoningResult> ReasonAsync(PatternTargetReasoningRequest request, CancellationToken cancellationToken)
        {
            Steps?.Add("reasoning");
            Seen = request;
            return Task.FromResult(Next);
        }
    }
}
