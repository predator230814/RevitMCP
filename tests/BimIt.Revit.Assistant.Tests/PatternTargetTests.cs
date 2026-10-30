using System.Text.Json;
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
        var (store, _) = Store(Studio(), "sr1.l.hidden-link-room");
        var reasoner = new FakeReasoner
        {
            Steps = steps,
            Next = new PatternTargetReasoningResult(false,
            [
                new PatternTargetRoomChoice("room_0", "compatible", "door_0",
                [
                    new PatternTargetEdgeBinding("pattern_2", "edge_2"),
                ]),
            ]),
        };
        var completion = await PatternTargetOrchestrator.CompleteAsync(store, reasoner, CancellationToken.None);
        steps.Add("review");
        Assert.Equal(["source", "discovery", "reasoning", "review"], steps);
        Assert.Equal(new PatternTargetCompletion(1, "studio", false, 1, 0, 0), completion);
        var review = store.PeekReviewJson() ?? "";
        var card = Card(store);
        Assert.Equal(PatternAdaptationStatus.Resolved, card.AdaptationStatus);
        Assert.Equal(
            new[]
            {
                "Entrance: door connected to Corridor",
                "Resolved",
                "✓ 1 switch at the entrance",
                "✓ 1 luminaire at room centre",
                "✓ receptacle pair → north wall",
                "✓ 1 data device opposite the entrance",
            },
            card.Reasoning!);
        Assert.DoesNotContain("door_0", review, StringComparison.Ordinal);
        Assert.DoesNotContain("edge_2", review, StringComparison.Ordinal);
        Assert.DoesNotContain("pattern_2", review, StringComparison.Ordinal);
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
        var pair = placed.Devices.Where(device => device.Role == "receptacle").ToArray();
        Assert.Equal(2, pair.Length);
        Assert.All(pair, device => Assert.InRange(device.YMetres, 5.5, 6));
        Assert.Equal(2, Math.Abs(pair[0].XMetres - pair[1].XMetres), 2);
        Assert.Equal(5, (pair[0].XMetres + pair[1].XMetres) / 2, 2);
        Assert.True(placed.Devices.Single(device => device.Role == "data").YMetres > 4);
        Assert.Contains("Corridor", reasoner.Seen!.Rooms[0].Doors[0].OtherSideName, StringComparison.Ordinal);
        Assert.Equal("Bathroom", reasoner.Seen.Rooms[0].Doors[1].OtherSideName);
    }

    [Fact]
    public async Task Two_pairs_on_two_source_walls_bind_to_two_target_walls()
    {
        var reasoner = new FakeReasoner
        {
            Next = new PatternTargetReasoningResult(false,
            [
                new PatternTargetRoomChoice("room_0", "compatible", "door_0",
                [
                    new PatternTargetEdgeBinding("pattern_1", "edge_2"),
                    new PatternTargetEdgeBinding("pattern_2", "edge_1"),
                ]),
            ]),
        };
        var (store, _) = Store(Studio(), "two-pairs", TwoPairs());
        var completion = await PatternTargetOrchestrator.CompleteAsync(store, reasoner, CancellationToken.None);
        Assert.Equal(1, completion!.Resolved);
        Assert.Equal("top", reasoner.Seen!.Patterns[1].Evidence!.Side);
        Assert.Equal("right", reasoner.Seen.Patterns[2].Evidence!.Side);
        Assert.NotEqual(reasoner.Seen.Patterns[1].Evidence!.SourceEdgeKey, reasoner.Seen.Patterns[2].Evidence!.SourceEdgeKey);
        var card = Card(store);
        Assert.Equal(PatternAdaptationStatus.Resolved, card.AdaptationStatus);
        Assert.Contains("✓ 1 switch at the entrance", card.Reasoning!);
        Assert.Contains("✓ receptacle pair → north wall", card.Reasoning!);
        Assert.Contains("✓ receptacle pair → east wall", card.Reasoning!);
        var receptacles = store.CurrentGeneration!.Rooms[0].Devices.Where(device => device.Role == "receptacle").ToArray();
        Assert.Equal(4, receptacles.Length);
        Assert.Equal(2, receptacles.Count(device => device.YMetres > 5.5 && device.XMetres < 9.5));
        Assert.Equal(2, receptacles.Count(device => device.XMetres > 9.5 && device.YMetres < 5.5));
    }

    [Fact]
    public async Task Invented_edge_keys_and_interior_bindings_fail_closed()
    {
        var reasoner = new FakeReasoner
        {
            Next = new PatternTargetReasoningResult(false,
            [
                new PatternTargetRoomChoice("room_0", "compatible", "door_0",
                [
                    new PatternTargetEdgeBinding("pattern_3", "edge_99"),
                    new PatternTargetEdgeBinding("pattern_1", "edge_2"),
                    new PatternTargetEdgeBinding("pattern_9", "edge_0"),
                    new PatternTargetEdgeBinding("rule_2", "edge_1"),
                ]),
            ]),
        };
        var (store, _) = Store(Studio(), "fail-closed", Interior());
        await PatternTargetOrchestrator.CompleteAsync(store, reasoner, CancellationToken.None);
        var card = Card(store);
        Assert.Equal(PatternAdaptationStatus.Partial, card.AdaptationStatus);
        Assert.Contains("✓ 1 switch at the entrance", card.Reasoning!);
        Assert.Contains("! receptacle pair on its corresponding wall: the proposed wall was not compatible", card.Reasoning!);
        Assert.Contains("! luminaire pair at room centre: the proposed wall was not compatible", card.Reasoning!);
        var devices = store.CurrentGeneration!.Rooms[0].Devices;
        Assert.DoesNotContain(devices, device => device.Role is "luminaire" or "receptacle");
        Assert.DoesNotContain(store.CurrentGeneration.Rooms, room => PatternAdaptationStatus.Selectable(room.AdaptationStatus));
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
        var invented = Card(inventedStore);
        Assert.Equal(PatternAdaptationStatus.Partial, invented.AdaptationStatus);
        Assert.Equal("Two possible entrance doors remain plausible", invented.Reasoning![0]);
        Assert.Contains(invented.Reasoning, line => line.StartsWith("! 1 switch at the entrance:", StringComparison.Ordinal));
        Assert.Contains("! receptacle pair on its corresponding wall: no corresponding wall was chosen", invented.Reasoning);
        Assert.Contains("✓ 1 luminaire at room centre", invented.Reasoning);

        var (uncertainStore, _) = Store(Studio(), "room-ref");
        await PatternTargetOrchestrator.CompleteAsync(uncertainStore, new FakeReasoner
        {
            Next = new PatternTargetReasoningResult(false,
            [
                new PatternTargetRoomChoice("room_0", "uncertain", null, [new PatternTargetEdgeBinding("pattern_2", "edge_2")]),
            ]),
        }, CancellationToken.None);
        var uncertain = Card(uncertainStore);
        Assert.Contains(uncertain.Reasoning!, line => line.StartsWith("! 1 switch at the entrance:", StringComparison.Ordinal));
        Assert.Contains("! receptacle pair on its corresponding wall: no corresponding wall was chosen", uncertain.Reasoning!);
    }

    [Fact]
    public async Task Three_resolved_and_one_failed_item_make_a_partial_room_that_stays_visible()
    {
        var (store, _) = Store(Studio(), "partial-ref");
        var completion = await PatternTargetOrchestrator.CompleteAsync(store, Choice("door_0"), CancellationToken.None);
        Assert.Equal(new PatternTargetCompletion(1, "studio", false, 0, 1, 0), completion);
        var parsed = Parsed(store);
        var card = Assert.Single(parsed.Rooms!);
        Assert.Equal(PatternAdaptationStatus.Partial, card.AdaptationStatus);
        Assert.NotEqual("rejected", card.Band);
        Assert.Equal(3, card.Devices!.Count);
        Assert.Empty(parsed.SuggestedRooms ?? []);
        Assert.Contains("Resolved", card.Reasoning!);
        Assert.Contains("Unresolved", card.Reasoning!);
        var hidden = Assert.Single(store.CurrentGeneration!.Rooms);
        Assert.Equal(PatternAdaptationStatus.Partial, hidden.AdaptationStatus);
        Assert.Equal(
            "BIM-IT found 1 candidate studio room.\nTarget adaptation is partial for 1.\nReview the Pattern Review panel for the proposed layouts.\nNo Revit changes were made.",
            PatternTargetCompletionText.Compose(completion!));
    }

    [Fact]
    public void Completion_text_is_deterministic_and_states_no_changes()
    {
        Assert.Equal(
            "BIM-IT found 6 candidate studio rooms.\nTarget adaptation completed for 5 and is partial for 1.\nReview the Pattern Review panel for the proposed layouts.\nNo Revit changes were made.",
            PatternTargetCompletionText.Compose(new PatternTargetCompletion(6, "studio", false, 5, 1, 0)));
        var unavailable = PatternTargetCompletionText.Compose(new PatternTargetCompletion(2, null, true, 0, 0, 0));
        Assert.StartsWith("BIM-IT found 2 candidate rooms.", unavailable, StringComparison.Ordinal);
        Assert.Contains("geometric comparison only", unavailable, StringComparison.Ordinal);
        Assert.EndsWith("No Revit changes were made.", unavailable, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unavailable_reasoner_keeps_the_geometric_review()
    {
        var (store, _) = Store(Studio(), "room-ref");
        var before = store.PeekReviewJson();
        var completion = await PatternTargetOrchestrator.CompleteAsync(store, new FakeReasoner { Next = PatternTargetReasoningResult.None }, CancellationToken.None);
        var after = store.PeekReviewJson() ?? "";
        Assert.True(completion!.Unavailable);
        Assert.Contains("target_reasoning_unavailable", after, StringComparison.Ordinal);
        Assert.Contains("User requested:", before ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public void Snake_case_reply_is_read_and_extra_fields_are_refused()
    {
        var reply = "Here is the result:\n```json\n{\"rooms\":[{\"room_key\":\"room_0\",\"assessment\":\"compatible\",\"entrance_feature_key\":\"door_0\",\"bindings\":[{\"pattern_key\":\"pattern_2\",\"target_edge_key\":\"edge_2\"}]}]}\n```";
        Assert.True(PatternTargetResponseParser.TryParse(reply, out var result));
        Assert.False(result.Unavailable);
        var room = Assert.Single(result.Rooms);
        Assert.Equal("room_0", room.RoomKey);
        Assert.Equal("door_0", room.EntranceFeatureKey);
        var binding = Assert.Single(room.Bindings);
        Assert.Equal("pattern_2", binding.PatternKey);
        Assert.Equal("edge_2", binding.EdgeKey);

        Assert.True(PatternTargetResponseParser.TryParse("{\"rooms\":[{\"room_key\":\"room_0\",\"assessment\":\"uncertain\",\"entrance_feature_key\":null}]}", out var uncertain));
        Assert.Null(Assert.Single(uncertain.Rooms).EntranceFeatureKey);

        Assert.False(PatternTargetResponseParser.TryParse("{\"rooms\":[{\"room_key\":\"room_0\",\"assessment\":\"compatible\",\"bindings\":[{\"rule_key\":\"rule_2\",\"target_edge_key\":\"edge_2\"}]}]}", out _));
        Assert.False(PatternTargetResponseParser.TryParse("{\"rooms\":[{\"room_key\":\"room_0\",\"assessment\":\"compatible\",\"x\":1.2,\"y\":3.4}]}", out _));
        Assert.False(PatternTargetResponseParser.TryParse("{\"rooms\":[{\"room_key\":\"room_0\",\"assessment\":\"approved\"}]}", out _));
        Assert.False(PatternTargetResponseParser.TryParse("no json here", out _));
    }

    [Fact]
    public void Composed_prompt_states_the_reply_shape_and_carries_no_identity()
    {
        var rooms = new[] { Studio() };
        var matches = RoomShapeMatcher.Match(Pattern(), rooms, resolveSemantics: false);
        var draft = PatternTargetContext.Build(Pattern(), rooms, matches, Review(), "studio");
        var prompt = PatternTargetContext.Compose(draft!.Request);
        Assert.Contains("\"room_key\"", prompt, StringComparison.Ordinal);
        Assert.Contains("entrance_feature_key", prompt, StringComparison.Ordinal);
        Assert.Contains("\"pattern_key\":\"pattern_2\"", prompt, StringComparison.Ordinal);
        Assert.Contains("\"anchor\":\"source_correspondence\"", prompt, StringComparison.Ordinal);
        Assert.Contains("\"arrangement\":\"symmetric_pair\"", prompt, StringComparison.Ordinal);
        Assert.Contains("\"source_edge_key\":\"source_edge_2\"", prompt, StringComparison.Ordinal);
        Assert.Contains("\"side\":\"north\"", prompt, StringComparison.Ordinal);
        Assert.Contains("Never bind an interior pattern", prompt, StringComparison.Ordinal);
        Assert.Contains("\"other_side_name\":\"Corridor\"", prompt, StringComparison.Ordinal);
        Assert.Contains("\"other_side_name\":\"Bathroom\"", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("ref-0", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("paired_symmetric", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("rule_key", prompt, StringComparison.Ordinal);
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
    public async Task Stale_generation_result_is_ignored()
    {
        var (store, pending) = Store(Studio(), "room-ref");
        var before = store.PeekReviewJson();
        Assert.False(store.CompleteTargetReasoning("ffffffffffffffffffffffffffffffff", PatternTargetResolution.Apply(pending, PatternTargetReasoningResult.None)));
        Assert.Equal(before, store.PeekReviewJson());

        var (replaced, _) = Store(Studio(), "room-ref");
        var newer = replaced.PeekReviewJson();
        var completion = await PatternTargetOrchestrator.CompleteAsync(replaced, new FakeReasoner
        {
            During = () => replaced.Replace(new RoomPatternRecord("artifact-2", 1, 5), newer),
            Next = new PatternTargetReasoningResult(false,
                [new PatternTargetRoomChoice("room_0", "compatible", "door_0", [new PatternTargetEdgeBinding("pattern_2", "edge_2")])]),
        }, CancellationToken.None);
        Assert.Null(completion);
        Assert.Equal(newer, replaced.PeekReviewJson());
    }

    [Fact]
    public void Target_context_is_bounded_normalized_and_identity_free()
    {
        var rooms = Enumerable.Range(0, 10).Select(index => Room(index, "Studio " + index.ToString())).ToArray();
        var matches = RoomShapeMatcher.Match(Pattern(), rooms, resolveSemantics: false);
        var draft = PatternTargetContext.Build(Pattern(), rooms, matches, Review(), "studio");
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
        Assert.Equal(4, draft.Request.Patterns.Count);
        Assert.All(draft.Request.Patterns, pattern => Assert.StartsWith("pattern_", pattern.PatternKey, StringComparison.Ordinal));
        Assert.Equal(PatternPlacements.Interior, draft.Request.Patterns[1].Evidence!.Placement);
        Assert.Equal(2, draft.Request.Patterns[2].Evidence!.DeviceCount);
        Assert.Equal("top", draft.Request.Patterns[2].Evidence!.Side);
        Assert.Equal(4, draft.Request.Source!.Edges.Count);
        Assert.Equal("source_edge_0", draft.Request.Source.Entrance?.EdgeKey);
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
    public async Task Mirrored_studio_follows_its_own_corridor_door()
    {
        var mirrored = Studio() with { Openings = [Door(9, 0, "Corridor", "C2"), Door(1, 0, "Bathroom", "B2")] };
        var (store, _) = Store(mirrored, "mirror-ref");
        await PatternTargetOrchestrator.CompleteAsync(store, Choice("door_0", new PatternTargetEdgeBinding("pattern_2", "edge_2")), CancellationToken.None);
        var devices = store.CurrentGeneration!.Rooms[0].Devices;
        var entrance = devices.Single(device => device.Role == "switch");
        Assert.InRange(entrance.XMetres, 7.5, 10);
        Assert.InRange(entrance.YMetres, 0, 1.5);
        var data = devices.Single(device => device.Role == "data");
        Assert.InRange(data.XMetres, 8, 10);
        Assert.True(data.YMetres > 4);
        Assert.Equal(PatternAdaptationStatus.Resolved, Card(store).AdaptationStatus);
    }

    [Fact]
    public async Task L_shaped_studio_with_wall_jogs_places_pair_and_data_on_the_bound_and_facing_walls()
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
        await PatternTargetOrchestrator.CompleteAsync(store, Choice("door_0", new PatternTargetEdgeBinding("pattern_2", "edge_6")), CancellationToken.None);
        var card = Card(store);
        Assert.Equal(PatternAdaptationStatus.Resolved, card.AdaptationStatus);
        Assert.Equal("Entrance: door connected to Corridor", card.Reasoning![0]);
        Assert.Contains("✓ receptacle pair → north wall", card.Reasoning);
        var devices = store.CurrentGeneration!.Rooms[0].Devices;
        var data = devices.Single(device => device.Role == "data");
        Assert.InRange(data.XMetres, 0.5, 1.5);
        Assert.InRange(data.YMetres, 9.5, 10);
        var pair = devices.Where(device => device.Role == "receptacle").ToArray();
        Assert.Equal(2, pair.Length);
        Assert.All(pair, device =>
        {
            Assert.InRange(device.YMetres, 9.5, 10);
            Assert.InRange(device.XMetres, 0.1, 8.9);
        });
        Assert.InRange(Math.Abs(pair[0].XMetres - pair[1].XMetres), 1.7, 1.85);
    }

    [Fact]
    public async Task A_hole_over_the_centre_keeps_the_luminaire_out()
    {
        var room = Studio() with { Holes = [Rectangle(4, 2, 6, 4)] };
        var (store, _) = Store(room, "hole-ref");
        await PatternTargetOrchestrator.CompleteAsync(store, Choice("door_0", new PatternTargetEdgeBinding("pattern_2", "edge_2")), CancellationToken.None);
        var card = Card(store);
        Assert.Equal(PatternAdaptationStatus.Partial, card.AdaptationStatus);
        Assert.Contains("! 1 luminaire at room centre: no valid position inside the room", card.Reasoning!);
        Assert.DoesNotContain(card.Devices!, device => device.Role == "luminaire");
    }

    [Fact]
    public void Interior_pair_stays_inside_the_room_and_out_of_holes()
    {
        var source = Interior();
        var cases = new (IReadOnlyList<PlanPoint> Loop, double X, double Y, IReadOnlyList<IReadOnlyList<PlanPoint>> Holes)[]
        {
            (Rectangle(0, 0, 10, 6), 5, 3, []),
            (Rectangle(0, 0, 10, 6), 5, 3, [Rectangle(4.6, 2.6, 5.4, 3.4)]),
            (Rectangle(0, 0, 6, 10), 3, 5, []),
            ([new(0, 0), new(6, 0), new(6, 3), new(3, 3), new(3, 6), new(0, 6)], 2.5, 2.5, []),
            (Rectangle(0, 0, 3, 2.4), 1.5, 1.2, []),
            (Rectangle(0, 0, 40, 20), 20, 10, []),
        };
        foreach (var (loop, x, y, holes) in cases)
        {
            var adaptation = SemanticPatternAdapter.Adapt(source, new SemanticPatternAdapter.Target
            {
                Loop = loop,
                Holes = holes,
                CentroidX = x,
                CentroidY = y,
            });
            var lights = adaptation.Placements.Where(placement => placement.Role == "luminaire").ToArray();
            Assert.Equal(2, lights.Length);
            Assert.All(lights, light =>
            {
                Assert.Equal(PatternPlacements.Interior, light.PlacementKind);
                Assert.True(Inside(loop, light.X, light.Y));
                Assert.True(DistanceToBoundary(loop, light.X, light.Y) >= SemanticPatternAdapter.InteriorClearance - 1e-6);
                Assert.All(holes, hole =>
                {
                    Assert.False(Inside(hole, light.X, light.Y));
                    Assert.True(DistanceToBoundary(hole, light.X, light.Y) >= SemanticPatternAdapter.InteriorClearance - 1e-6);
                });
            });
            Assert.Equal(x, (lights[0].X + lights[1].X) / 2, 3);
            Assert.Equal(y, (lights[0].Y + lights[1].Y) / 2, 3);
            var spacing = Math.Sqrt(Math.Pow(lights[0].X - lights[1].X, 2) + Math.Pow(lights[0].Y - lights[1].Y, 2));
            Assert.InRange(spacing, (2 * SemanticPatternAdapter.MinPairHalf) - 1e-3, (2 * SemanticPatternAdapter.MaxInteriorPairHalf) + 1e-3);
            Assert.DoesNotContain(adaptation.Outcomes, outcome => outcome.Spec.Role == "luminaire" && !outcome.Resolved);
        }

        var corridor = SemanticPatternAdapter.Adapt(source, new SemanticPatternAdapter.Target
        {
            Loop = Rectangle(0, 0, 8, 0.5),
            CentroidX = 4,
            CentroidY = 0.25,
        });
        Assert.DoesNotContain(corridor.Placements, placement => placement.Role == "luminaire");
        Assert.Contains(corridor.Outcomes, outcome => outcome.Spec.Role == "luminaire" && outcome.Failure == SemanticPatternAdapter.Failures.OutsideRoom);
    }

    [Fact]
    public void Room_centre_is_the_geometric_centre_not_the_revit_room_point()
    {
        var source = Interior();
        var offCentre = SemanticPatternAdapter.Adapt(source, new SemanticPatternAdapter.Target
        {
            Loop = Rectangle(0, 0, 10, 6),
            CentroidX = 2,
            CentroidY = 5,
        });
        var lights = offCentre.Placements.Where(placement => placement.Role == "luminaire").ToArray();
        Assert.Equal(2, lights.Length);
        Assert.Equal(5, (lights[0].X + lights[1].X) / 2, 3);
        Assert.Equal(3, (lights[0].Y + lights[1].Y) / 2, 3);

        var uShape = new SemanticPatternAdapter.Target
        {
            Loop = [new(0, 0), new(9, 0), new(9, 6), new(6, 6), new(6, 1), new(3, 1), new(3, 6), new(0, 6)],
            CentroidX = 1.5,
            CentroidY = 3,
        };
        Assert.Equal(new PlanPoint(1.5, 3), SemanticPatternAdapter.Centre(uShape));
    }

    [Fact]
    public async Task Reasoning_wording_comes_from_the_role_and_never_shows_keys()
    {
        var (store, _) = Store(Studio(), "wording", Interior());
        await PatternTargetOrchestrator.CompleteAsync(store, Choice("door_0", new PatternTargetEdgeBinding("pattern_3", "edge_2")), CancellationToken.None);
        var card = Card(store);
        Assert.Contains("✓ 2 luminaires around room centre", card.Reasoning!);
        Assert.DoesNotContain(card.Reasoning!, line => line.Contains("luminaire pair", StringComparison.Ordinal) && line.StartsWith('✓'));
        Assert.All(card.Reasoning!, line =>
        {
            Assert.DoesNotContain("pattern_", line, StringComparison.Ordinal);
            Assert.DoesNotContain("edge_", line, StringComparison.Ordinal);
            Assert.DoesNotContain("door_", line, StringComparison.Ordinal);
            Assert.DoesNotContain("room_", line, StringComparison.Ordinal);
        });
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
        Assert.Equal(8, AssistantModelTools.Names.Length);
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

    private static FakeReasoner Choice(string door, params PatternTargetEdgeBinding[] bindings)
    {
        return new FakeReasoner
        {
            Next = new PatternTargetReasoningResult(false, [new PatternTargetRoomChoice("room_0", "compatible", door, bindings)]),
        };
    }

    private static (RoomPatternStore Store, PatternTargetPending Pending) Store(CandidateRoom room, string roomRef, SourceRoomPattern? pattern = null)
    {
        room = room with { ElementRef = roomRef };
        var source = pattern ?? Pattern();
        source = source with { Evidence = PatternEvidence.Describe(source) };
        var matches = RoomShapeMatcher.Match(source, [room], resolveSemantics: false);
        var review = Review();
        var draft = PatternTargetContext.Build(source, [room], matches, review, "studio");
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

    private static RoomPatternReview.Review Parsed(RoomPatternStore store)
    {
        var json = store.PeekReviewJson();
        Assert.NotNull(json);
        return JsonSerializer.Deserialize<RoomPatternReview.Review>(json!, RoomPatternReview.Options)!;
    }

    private static RoomPatternReview.RoomCard Card(RoomPatternStore store)
    {
        return Assert.Single(Parsed(store).Rooms!);
    }

    private static SourceRoomPattern Pattern()
    {
        return new SourceRoomPattern(
            Rectangle(0, 0, 10, 6),
            new PlanPoint(1, 0),
            [
                new SourceDevice("switch", 1, 0.2, "boundary", null),
                new SourceDevice("luminaire", 5, 3, "interior", null),
                new SourceDevice("receptacle", 4, 5.8, "boundary", null),
                new SourceDevice("receptacle", 6, 5.8, "boundary", null),
                new SourceDevice("data", 5, 5.8, "boundary", null),
            ],
            [
                SemanticPatternTests.Spec("switch", PatternAnchors.Entrance, offset: 0.15, basis: PatternBasis.UserStated, devices: [0]),
                SemanticPatternTests.Spec("luminaire", PatternAnchors.RoomCenter, basis: PatternBasis.UserStated, devices: [1]),
                SemanticPatternTests.Spec("receptacle", PatternAnchors.SourceCorrespondence, pair: true, group: "pair", devices: [2, 3]),
                SemanticPatternTests.Spec("data", PatternAnchors.OppositeBoundary, offset: 0.2, basis: PatternBasis.UserStated, devices: [4]),
            ]);
    }

    private static SourceRoomPattern TwoPairs()
    {
        return new SourceRoomPattern(
            Rectangle(0, 0, 10, 6),
            new PlanPoint(1, 0),
            [
                new SourceDevice("switch", 1, 0.2, "boundary", null),
                new SourceDevice("receptacle", 3, 5.8, "boundary", null),
                new SourceDevice("receptacle", 5, 5.8, "boundary", null),
                new SourceDevice("receptacle", 9.8, 2, "boundary", null),
                new SourceDevice("receptacle", 9.8, 4, "boundary", null),
            ],
            [
                SemanticPatternTests.Spec("switch", PatternAnchors.Entrance, devices: [0]),
                SemanticPatternTests.Spec("receptacle", PatternAnchors.SourceCorrespondence, pair: true, devices: [1, 2]),
                SemanticPatternTests.Spec("receptacle", PatternAnchors.SourceCorrespondence, pair: true, devices: [3, 4]),
            ]);
    }

    private static SourceRoomPattern Interior()
    {
        return new SourceRoomPattern(
            Rectangle(0, 0, 10, 6),
            new PlanPoint(1, 0),
            [
                new SourceDevice("switch", 1, 0.2, "boundary", null),
                new SourceDevice("luminaire", 4, 3, "interior", null),
                new SourceDevice("luminaire", 6, 3, "interior", null),
                new SourceDevice("receptacle", 4, 5.8, "boundary", null),
                new SourceDevice("receptacle", 6, 5.8, "boundary", null),
            ],
            [
                SemanticPatternTests.Spec("switch", PatternAnchors.Entrance, devices: [0]),
                SemanticPatternTests.Spec("luminaire", PatternAnchors.RoomCenter, pair: true, devices: [1, 2]),
                SemanticPatternTests.Spec("data", PatternAnchors.OppositeBoundary),
                SemanticPatternTests.Spec("receptacle", PatternAnchors.SourceCorrespondence, pair: true, devices: [3, 4]),
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
            Understood = ["User requested:", "1 switch · entrance"],
            Rooms = [],
        };
    }

    private static List<PlanPoint> Rectangle(double x0, double y0, double x1, double y1)
    {
        return [new PlanPoint(x0, y0), new PlanPoint(x1, y0), new PlanPoint(x1, y1), new PlanPoint(x0, y1)];
    }

    private static bool Inside(IReadOnlyList<PlanPoint> loop, double x, double y)
    {
        var inside = false;
        for (int i = 0, j = loop.Count - 1; i < loop.Count; j = i++)
        {
            if ((loop[i].Y > y) != (loop[j].Y > y)
                && x < ((loop[j].X - loop[i].X) * (y - loop[i].Y) / (loop[j].Y - loop[i].Y)) + loop[i].X)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    private static double DistanceToBoundary(IReadOnlyList<PlanPoint> loop, double x, double y)
    {
        var best = double.MaxValue;
        for (var index = 0; index < loop.Count; index++)
        {
            var start = loop[index];
            var end = loop[(index + 1) % loop.Count];
            var dx = end.X - start.X;
            var dy = end.Y - start.Y;
            var t = Math.Clamp((((x - start.X) * dx) + ((y - start.Y) * dy)) / ((dx * dx) + (dy * dy)), 0, 1);
            var px = start.X + (t * dx) - x;
            var py = start.Y + (t * dy) - y;
            best = Math.Min(best, Math.Sqrt((px * px) + (py * py)));
        }

        return best;
    }

    private sealed class FakeReasoner : IPatternTargetReasoner
    {
        public List<string>? Steps { get; init; }

        public Action? During { get; init; }

        public PatternTargetReasoningResult Next { get; init; } = PatternTargetReasoningResult.None;

        public PatternTargetReasoningRequest? Seen { get; private set; }

        public Task<PatternTargetReasoningResult> ReasonAsync(PatternTargetReasoningRequest request, CancellationToken cancellationToken)
        {
            Steps?.Add("reasoning");
            Seen = request;
            During?.Invoke();
            return Task.FromResult(Next);
        }
    }
}
