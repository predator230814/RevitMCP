using System.Text.Json;
using BimIt.Revit.Assistant.Ipc;
using BimIt.Revit.Assistant.Patterns;
using BimIt.Revit.Contracts;
using Xunit;

namespace BimIt.Revit.Assistant.Tests;

public sealed class RoomPatternTests
{
    private const string InstanceId = "11111111-2222-3333-4444-555555555555";

    [Fact]
    public void Same_room_matches_after_translation_without_a_global_calibration()
    {
        var source = Pattern(Rectangle(0.1, 0.2, 0.5, 0.4), new PlanPoint(0.3, 0.2));
        var room = Room("Bedroom", "201", "Level 2", Rectangle(100, 40, 104, 42), new PlanPoint(102, 40));
        var match = Assert.Single(RoomShapeMatcher.Match(source, [room]));
        Assert.Equal("high", match.Band);
        Assert.True(match.Score >= RoomShapeMatcher.HighConfidence);
        Assert.Equal(0, match.RotationDegrees);
        Assert.False(match.Mirrored);
        Assert.Null(match.Reason);
        var device = Assert.Single(match.Devices);
        Assert.InRange(device.NormalizedX, 0, 1);
        Assert.InRange(device.NormalizedY, 0, 1);
        Assert.NotEqual(source.Devices[0].X, device.LocalX);
    }

    [Fact]
    public void Rotated_rectangle_is_reported_without_claiming_a_mirror()
    {
        var source = Pattern(Rectangle(0, 0, 4, 2), new PlanPoint(2, 0));
        var room = Room("Bedroom", "202", "Level 2", Rectangle(0, 0, 2, 4), new PlanPoint(1, 0));
        var match = Assert.Single(RoomShapeMatcher.Match(source, [room]));
        Assert.Equal("high", match.Band);
        Assert.Contains(match.RotationDegrees, new[] { 90, 270 });
        Assert.False(match.Mirrored);
    }

    [Fact]
    public void Mirrored_l_shape_is_reported_as_mirrored()
    {
        var source = Pattern(L(0, 0, false), null);
        var same = Assert.Single(RoomShapeMatcher.Match(source, [Room("Bedroom", "203", "Level 2", L(30, 80, false), null)]));
        Assert.Equal("high", same.Band);
        Assert.False(same.Mirrored);
        var room = Room("Bedroom", "204", "Level 2", L(30, 80, true), null);
        var match = Assert.Single(RoomShapeMatcher.Match(source, [room]));
        Assert.Equal("high", match.Band);
        Assert.True(match.Score >= RoomShapeMatcher.HighConfidence);
        Assert.True(match.Mirrored);
        Assert.Null(match.Reason);
    }

    [Fact]
    public void Unsupported_shapes_fail_closed()
    {
        var source = Pattern(Rectangle(0, 0, 4, 2), null);
        var diagonal = Room("A", "1", "L1", [new PlanPoint(0, 1), new PlanPoint(1, 0), new PlanPoint(0, -1), new PlanPoint(-1, 0)], null);
        var loops = new CandidateRoom("B", "2", "L1", 0, 0, Rectangle(0, 0, 4, 2), true, false, []);
        var complex = new CandidateRoom("C", "3", "L1", 0, 0, Rectangle(0, 0, 4, 2), false, true, []);
        var wide = Room("D", "4", "L1", Rectangle(0, 0, 8, 1), null);
        var matches = RoomShapeMatcher.Match(source, [diagonal, loops, complex, wide]);
        Assert.Equal("non_orthogonal_boundary", matches[0].Reason);
        Assert.Equal("multiple_boundary_loops", matches[1].Reason);
        Assert.Equal("boundary_too_complex", matches[2].Reason);
        Assert.Equal("rejected", matches[0].Band);
        Assert.Equal("rejected", matches[3].Band);
        Assert.Contains(matches[3].Reason, new[] { "aspect_mismatch", "low_shape_similarity" });
    }

    [Fact]
    public async Task Proposal_schema_rejects_revit_identity_and_invented_scores()
    {
        var tool = Tool(static () => null);
        var extra = await tool.InvokeAsync(Json("{\"page_number\":1,\"boundary\":[{\"x\":0,\"y\":0},{\"x\":1,\"y\":0},{\"x\":0,\"y\":1}],\"devices\":[{\"role\":\"receptacle\",\"page_x\":0.2,\"page_y\":0.2,\"placement_kind\":\"boundary\"}],\"element_ref\":\"secret\",\"score\":0.99}"), CancellationToken.None);
        Assert.False(Assert.IsType<bool>(extra["stored"]));
        Assert.Equal(RoomPatternFailures.Shape, extra["code"]);

        var role = await tool.InvokeAsync(Json(ValidBody().Replace("receptacle", "wall", StringComparison.Ordinal)), CancellationToken.None);
        Assert.Equal(RoomPatternFailures.Role, role["code"]);

        var placement = await tool.InvokeAsync(Json(ValidBody().Replace("\"placement_kind\":\"boundary\"", "\"placement_kind\":\"ceiling\"", StringComparison.Ordinal)), CancellationToken.None);
        Assert.Equal(RoomPatternFailures.Placement, placement["code"]);

        var missing = await tool.InvokeAsync(Json(ValidBody()), CancellationToken.None);
        Assert.Equal(RoomPatternFailures.Attachment, missing["code"]);
        Assert.Contains("page", Assert.IsType<string>(missing["reason"]), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(4, tool.ConsumeUsedTools().Count);
    }

    [Fact]
    public async Task Legacy_x_y_points_return_an_actionable_coordinate_reason()
    {
        var tool = Tool(static () => null);
        var result = await tool.InvokeAsync(Json("""
            {"page_number":1,"boundary":[{"x":0.1,"y":0.1},{"x":0.7,"y":0.1},{"x":0.1,"y":0.4}],"devices":[{"role":"receptacle","page_x":0.2,"page_y":0.2,"placement_kind":"boundary"}]}
            """), CancellationToken.None);
        Assert.Equal(false, result["stored"]);
        Assert.Equal(RoomPatternFailures.Coordinate, result["code"]);
        var reason = Assert.IsType<string>(result["reason"]);
        Assert.Contains("page_x", reason, StringComparison.Ordinal);
        Assert.Contains("page_y", reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Room_name_accepts_a_word_and_rejects_an_identity()
    {
        var tool = Tool(static () => null);
        var named = await tool.InvokeAsync(Json("""
            {"page_number":1,"room_name":"studio","boundary":[{"page_x":0.1,"page_y":0.1},{"page_x":0.7,"page_y":0.1},{"page_x":0.7,"page_y":0.4},{"page_x":0.1,"page_y":0.4}],"devices":[{"role":"receptacle","page_x":0.2,"page_y":0.25,"placement_kind":"boundary"}]}
            """), CancellationToken.None);
        Assert.Equal(RoomPatternFailures.Attachment, named["code"]);

        var identity = await tool.InvokeAsync(Json("""
            {"page_number":1,"room_name":"../studio","boundary":[{"page_x":0.1,"page_y":0.1},{"page_x":0.7,"page_y":0.1},{"page_x":0.7,"page_y":0.4},{"page_x":0.1,"page_y":0.4}],"devices":[{"role":"receptacle","page_x":0.2,"page_y":0.25,"placement_kind":"boundary"}]}
            """), CancellationToken.None);
        Assert.Equal(RoomPatternFailures.RoomName, identity["code"]);
    }

    [Fact]
    public void Named_rooms_stay_selectable_when_the_shape_only_differs()
    {
        var low = new RoomShapeMatch("Studio 1", "L1", "rejected", 0.2, 90, false, "low_shape_similarity", [], []);
        var promoted = RoomPatternGrounding.PromoteNamedRooms([low], "studio");
        Assert.Equal("lower", promoted[0].Band);
        Assert.Equal("named_room", promoted[0].Reason);

        var curved = new RoomShapeMatch("Studio 2", "L1", "rejected", 0, 0, false, "non_orthogonal_boundary", [], []);
        var kept = RoomPatternGrounding.PromoteNamedRooms([curved], "studio");
        Assert.Equal("rejected", kept[0].Band);
        Assert.Equal("rejected", RoomPatternGrounding.PromoteNamedRooms([low], null)[0].Band);
    }

    [Fact]
    public async Task Stored_pattern_hides_revit_identities_from_the_model_and_review()
    {
        const string secretRef = "secret-room-ref";
        const string secretDocument = "secret-doc-id";
        var artifact = new AssistantDocumentArtifact(
            "sketch.png",
            AssistantDocumentKind.Image,
            4,
            null,
            false,
            [new AssistantVisualPage(1, "image/png", [1, 2, 3, 4], 100, 100)],
            false,
            1);
        var store = new RoomPatternStore();
        var session = new ScriptedSession(secretDocument, secretRef);
        var tool = new RoomPatternProposalTool(session, InstanceId, store, () => artifact);
        var result = await tool.InvokeAsync(Json(ValidBody()), CancellationToken.None);
        var model = JsonSerializer.Serialize(result);
        Assert.Equal(true, result["stored"]);
        Assert.DoesNotContain(secretRef, model, StringComparison.Ordinal);
        Assert.DoesNotContain(secretDocument, model, StringComparison.Ordinal);
        Assert.DoesNotContain("element_ref", model, StringComparison.Ordinal);
        Assert.DoesNotContain("instance_id", model, StringComparison.Ordinal);
        var review = store.PeekReviewJson();
        Assert.NotNull(review);
        Assert.DoesNotContain(secretRef, review, StringComparison.Ordinal);
        Assert.DoesNotContain(secretDocument, review, StringComparison.Ordinal);
        Assert.DoesNotContain("element_ref", review, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-type-ref", review, StringComparison.Ordinal);
        Assert.DoesNotContain("family_type_ref", review, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-type-ref", model, StringComparison.Ordinal);
        Assert.Contains("\"readState\":\"matched\"", review, StringComparison.Ordinal);
        Assert.Equal("secret-type-ref", store.CurrentGeneration!.Options["t0"].FamilyTypeRef);
        Assert.Contains(secretRef, store.CurrentGeneration.Rooms.Select(room => room.RoomRef));
        Assert.True(RoomPatternReview.TryParse(review, out _));
    }

    [Fact]
    public void Attachment_replacement_removal_and_clear_drop_the_pattern()
    {
        var store = new RoomPatternStore();
        store.Replace(new RoomPatternRecord("sketch-a", 1, 1), ReviewJson());
        store.NoteAttachmentIdentityChanged("sketch-a", "sketch-a");
        Assert.NotNull(store.PeekReviewJson());
        store.NoteAttachmentIdentityChanged("sketch-a", "sketch-b");
        Assert.Null(store.PeekReviewJson());

        store.Replace(new RoomPatternRecord("sketch-b", 1, 1), ReviewJson());
        store.NoteAttachmentIdentityChanged("sketch-b", null);
        Assert.Null(store.PeekReviewJson());

        store.Replace(new RoomPatternRecord("sketch-c", 1, 1), ReviewJson());
        store.Clear();
        Assert.Null(store.PeekReviewJson());

        var host = File.ReadAllText(Path.Combine(RepoRoot(), "src", "BimIt.Revit.Assistant", "AssistantHost.cs"));
        Assert.Contains("roomPatterns.NoteAttachmentIdentityChanged", host, StringComparison.Ordinal);
        Assert.Contains("roomPatterns.Clear()", host, StringComparison.Ordinal);
        var runtime = File.ReadAllText(Path.Combine(RepoRoot(), "src", "BimIt.Revit.Assistant", "Providers", "GitHubCopilot", "GitHubCopilotAgentRuntime.cs"));
        Assert.Contains("_patternStore.Clear()", runtime, StringComparison.Ordinal);
    }

    [Fact]
    public void Model_surface_stays_seven_tools_and_room_layouts_stay_a_read()
    {
        Assert.Equal(7, AssistantModelTools.Names.Length);
        Assert.Contains(RoomPatternProposalTool.Name, AssistantModelTools.Names);
        Assert.DoesNotContain(AssistantTools.GetRoomLayouts, AssistantModelTools.Names);
        Assert.Contains(AssistantTools.GetRoomLayouts, AssistantTools.AllowedReadTools);
        Assert.Contains(AssistantTools.GetMepDeviceTypes, AssistantTools.AllowedReadTools);
        Assert.DoesNotContain(AssistantTools.GetMepDeviceTypes, AssistantModelTools.Names);
        Assert.False(AssistantTools.IsTrustedWriteTool(AssistantTools.GetRoomLayouts));
        Assert.False(AssistantTools.IsTrustedWriteTool(AssistantTools.GetMepDeviceTypes));
        Assert.True(AssistantTools.IsTrustedWriteTool("revit_preview_room_pattern_placements"));
        Assert.False(AssistantIpc.IsModelToolName("revit_preview_room_pattern_placements"));
        Assert.False(AssistantTools.IsTrustedWriteTool(RoomPatternProposalTool.Name));
        Assert.True(AssistantIpc.IsModelToolName(RoomPatternProposalTool.Name));
        Assert.False(AssistantIpc.IsModelToolName(AssistantTools.GetRoomLayouts));
        Assert.False(AssistantIpc.IsModelToolName("revit_apply_mep_device_placements"));
    }

    [Fact]
    public void WebView_review_rejects_hidden_identities_and_has_no_write_authority()
    {
        var review = ReviewJson();
        var accepted = AssistantIpc.Serialize(new AssistantIpcMessage(
            AssistantIpcKind.ChatResult,
            null,
            null,
            null,
            false,
            "chat-1",
            null,
            null,
            null,
            null,
            null,
            null,
            "BIM-IT Assistant",
            "Pattern ready.",
            false,
            PatternReview: review));
        var parsed = AssistantIpc.Parse(accepted);
        Assert.True(parsed.Accepted);
        Assert.Equal(review, parsed.Message!.PatternReview);

        var leaked = review.Replace("Bedroom", "element_ref", StringComparison.Ordinal);
        var rejected = AssistantIpc.Serialize(new AssistantIpcMessage(
            AssistantIpcKind.ChatResult,
            null,
            null,
            null,
            false,
            "chat-1",
            null,
            null,
            null,
            null,
            null,
            null,
            "BIM-IT Assistant",
            "Pattern ready.",
            false,
            PatternReview: leaked));
        Assert.False(AssistantIpc.Parse(rejected).Accepted);
        Assert.False(AssistantIpc.Parse("{\"type\":\"hello\",\"instanceId\":\"abc\",\"patternReview\":\"{}\"}").Accepted);
        Assert.False(RoomPatternReview.TryParse(review.Replace("Bedroom", "C:\\\\secret", StringComparison.Ordinal), out _));

        var script = File.ReadAllText(Path.Combine(RepoRoot(), "src", "BimIt.Revit.Addin", "Assistant", "Ui", "app.js"));
        var start = script.IndexOf("function renderPattern", StringComparison.Ordinal);
        var end = script.IndexOf("function patternImage", StringComparison.Ordinal);
        var renderer = script[start..end];
        Assert.DoesNotContain("postMessage", renderer, StringComparison.Ordinal);
        Assert.Contains("data-room", renderer, StringComparison.Ordinal);
        var page = File.ReadAllText(Path.Combine(RepoRoot(), "src", "BimIt.Revit.Addin", "Assistant", "Ui", "index.html"));
        Assert.Contains("img-src 'self' data:", page, StringComparison.Ordinal);
        Assert.DoesNotContain("http:", page, StringComparison.Ordinal);
        Assert.DoesNotContain("https:", page, StringComparison.Ordinal);
    }

    private static RoomPatternProposalTool Tool(Func<AssistantDocumentArtifact?> attachment)
    {
        return new RoomPatternProposalTool(new ScriptedSession(null, null), InstanceId, new RoomPatternStore(), attachment);
    }

    private static SourceRoomPattern Pattern(IReadOnlyList<PlanPoint> boundary, PlanPoint? entrance)
    {
        return new SourceRoomPattern(boundary, entrance, [new SourceDevice("receptacle", 0.2, 0.2, "interior", null)]);
    }

    private static CandidateRoom Room(string name, string number, string level, IReadOnlyList<PlanPoint> loop, PlanPoint? door)
    {
        var centroid = Centroid(loop);
        var openings = door is PlanPoint point ? new[] { new CandidateOpening(point.X, point.Y, "door") } : Array.Empty<CandidateOpening>();
        return new CandidateRoom(name, number, level, centroid.X, centroid.Y, loop, false, false, openings);
    }

    private static PlanPoint Centroid(IReadOnlyList<PlanPoint> loop)
    {
        return new PlanPoint(loop.Average(point => point.X), loop.Average(point => point.Y));
    }

    private static IReadOnlyList<PlanPoint> Rectangle(double x0, double y0, double x1, double y1)
    {
        return [new PlanPoint(x0, y0), new PlanPoint(x1, y0), new PlanPoint(x1, y1), new PlanPoint(x0, y1)];
    }

    private static IReadOnlyList<PlanPoint> L(double x, double y, bool mirrored)
    {
        PlanPoint[] shape = [new(0, 0), new(6, 0), new(6, 2), new(4, 2), new(4, 4), new(0, 4)];
        if (mirrored)
        {
            shape = shape.Select(point => new PlanPoint(6 - point.X, point.Y)).Reverse().ToArray();
        }

        return shape.Select(point => new PlanPoint(point.X + x, point.Y + y)).ToArray();
    }

    private static string ValidBody()
    {
        return """
            {
              "page_number": 1,
              "boundary": [{"page_x":0.1,"page_y":0.1},{"page_x":0.7,"page_y":0.1},{"page_x":0.7,"page_y":0.4},{"page_x":0.1,"page_y":0.4}],
              "entrance": {"page_x":0.4,"page_y":0.1},
              "devices": [{"role":"receptacle","page_x":0.2,"page_y":0.25,"placement_kind":"boundary","family_hint":"duplex","evidence":"symbol","confidence":0.8}]
            }
            """;
    }

    private static JsonElement Json(string json) => JsonSerializer.Deserialize<JsonElement>(json);

    private static string ReviewJson()
    {
        var json = RoomPatternReview.Serialize(new RoomPatternReview.Review
        {
            PageNumber = 1,
            ReadState = "matched",
            CandidateCount = 1,
            HighCount = 1,
            Boundary = [new RoomPatternReview.Point { X = 0.1, Y = 0.1 }, new RoomPatternReview.Point { X = 0.7, Y = 0.1 }, new RoomPatternReview.Point { X = 0.1, Y = 0.4 }],
            Devices = [new RoomPatternReview.DevicePoint { Role = "receptacle", Placement = "interior", X = 0.2, Y = 0.2 }],
            Rooms =
            [
                new RoomPatternReview.RoomCard
                {
                    Label = "Bedroom 201",
                    Level = "Level 2",
                    Band = "high",
                    Score = 0.91,
                    RotationDegrees = 0,
                    Outline = [new RoomPatternReview.Point { X = 0, Y = 0 }, new RoomPatternReview.Point { X = 1, Y = 0 }, new RoomPatternReview.Point { X = 1, Y = 1 }],
                    Devices = [new RoomPatternReview.DevicePoint { Role = "receptacle", Placement = "interior", X = 0.5, Y = 0.5 }],
                },
            ],
        });
        Assert.NotNull(json);
        return json;
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "BimIt.Revit.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate BimIt.Revit.sln.");
    }

    private sealed class ScriptedSession(string? documentId, string? roomRef) : IAssistantToolSession
    {
        public Task<AssistantMcpReadiness> InitializeAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new AssistantMcpReadiness(true, 15, [], null));

        public Task<AssistantToolCall> CallAsync(string toolName, IReadOnlyDictionary<string, object?> arguments, CancellationToken cancellationToken)
        {
            if (documentId is null)
            {
                return Task.FromResult(new AssistantToolCall(true, "unused", "unused", null));
            }

            JsonElement payload = toolName switch
            {
                AssistantTools.GetContext => JsonSerializer.SerializeToElement(new GetContextResult
                {
                    Instance = new GetContextInstance { InstanceId = "wire-instance", RevitVersion = "2026", RevitBuild = "26.5.0.55" },
                    Document = new GetContextDocument
                    {
                        DocumentId = documentId,
                        Title = "Sample",
                        Kind = GetContextDocumentKind.Project,
                        IsWorkshared = false,
                        IsModelInCloud = false,
                        IsReadOnly = false,
                        IsModified = false,
                    },
                    Selection = new GetContextSelection { Count = 0 },
                }, ContractJson.Options),
                AssistantTools.QueryElements => JsonSerializer.SerializeToElement(new QueryElementsResult
                {
                    Context = new QueryElementsContext { InstanceId = "wire-instance", DocumentId = documentId },
                    MatchedCount = 1,
                    Truncated = false,
                    ElementRefs = [roomRef!],
                }, ContractJson.Options),
                AssistantTools.GetMepDeviceTypes => JsonSerializer.SerializeToElement(new GetMepDeviceTypesResult
                {
                    Context = new GetMepDeviceTypesContext { InstanceId = "wire-instance", DocumentId = documentId },
                    Truncated = false,
                    Items =
                    [
                        new MepDeviceTypeItem
                        {
                            FamilyTypeRef = "secret-type-ref",
                            CategoryToken = "OST_ElectricalFixtures",
                            CategoryName = "Electrical Fixtures",
                            FamilyName = "Duplex",
                            TypeName = "Standard",
                            PlacementClassification = "one_level_based_hosted",
                            Active = true,
                        },
                    ],
                }, ContractJson.Options),
                AssistantTools.GetRoomLayouts => JsonSerializer.SerializeToElement(new GetRoomLayoutsResult
                {
                    Context = new GetRoomLayoutsContext { InstanceId = "wire-instance", DocumentId = documentId },
                    LengthUnit = "m",
                    ChordToleranceMetres = 0.05,
                    OpeningScanTruncated = false,
                    Items =
                    [
                        new GetRoomLayoutItem
                        {
                            ElementRef = roomRef!,
                            Status = RoomLayoutItemStatus.Ok,
                            Name = "Bedroom",
                            Number = "201",
                            LevelName = "Level 2",
                            CentroidXMetres = 12,
                            CentroidYMetres = 8,
                            LoopsTruncated = false,
                            FeaturesTruncated = false,
                            Loops =
                            [
                                new GetRoomLayoutLoop
                                {
                                    VerticesTruncated = false,
                                    Vertices =
                                    [
                                        new GetRoomLayoutVertex { XMetres = 10, YMetres = 7 },
                                        new GetRoomLayoutVertex { XMetres = 14, YMetres = 7 },
                                        new GetRoomLayoutVertex { XMetres = 14, YMetres = 9 },
                                        new GetRoomLayoutVertex { XMetres = 10, YMetres = 9 },
                                    ],
                                },
                            ],
                            Features = [new GetRoomLayoutFeature { Kind = RoomFeatureKind.Door, XMetres = 12, YMetres = 7, FacingX = 0, FacingY = 1, WidthMetres = 0.9 }],
                        },
                    ],
                }, ContractJson.Options),
                _ => default,
            };
            return Task.FromResult(new AssistantToolCall(false, null, null, payload));
        }
    }
}
