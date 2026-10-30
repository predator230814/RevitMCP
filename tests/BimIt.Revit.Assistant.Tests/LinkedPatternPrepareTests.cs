using System.Text.Json;
using BimIt.Revit.Assistant.Patterns;
using BimIt.Revit.Contracts;
using Xunit;

namespace BimIt.Revit.Assistant.Tests;

public sealed class LinkedPatternPrepareTests
{
    private const string Instance = "11111111-2222-3333-4444-555555555555";

    private static readonly string LinkedRoom = SpatialRoomRefCodec.Link("link-unique", "room-unique");

    [Fact]
    public async Task Linked_room_with_a_work_plane_type_is_sent_to_preview()
    {
        var (store, preview) = Setup();
        var outcome = await Orchestrator(store, preview).PrepareAsync(Configuration(wall: "t1"), CancellationToken.None);

        Assert.False(outcome.IsError);
        var request = Assert.Single(preview.Requests);
        Assert.All(request.Placements!, placement => Assert.Equal(LinkedRoom, placement.RoomRef));
        Assert.Contains(request.Placements!, placement => placement.PlacementKind == "boundary" && placement.FamilyTypeRef == "type-face");
    }

    [Fact]
    public async Task Linked_wall_role_cannot_use_a_wall_hosted_type()
    {
        var (store, preview) = Setup();
        var outcome = await Orchestrator(store, preview).PrepareAsync(Configuration(wall: "t2"), CancellationToken.None);

        Assert.True(outcome.IsError);
        Assert.Contains("work-plane-based", outcome.Body, StringComparison.Ordinal);
        Assert.Empty(preview.Requests);
    }

    [Fact]
    public async Task A_selected_lower_band_room_can_be_prepared()
    {
        var store = new RoomPatternStore();
        store.Replace(new RoomPatternRecord("artifact", 1, 2), null, Generation(LinkedRoom, band: "lower"));
        var preview = new FakePreview();

        var outcome = await Orchestrator(store, preview).PrepareAsync(Configuration(wall: "t1"), CancellationToken.None);

        Assert.False(outcome.IsError);
        Assert.Single(preview.Requests);
    }

    [Fact]
    public void Host_room_keeps_wall_hosted_types()
    {
        var generation = Generation("host-room-unique");
        var configuration = new PatternLocalConfiguration
        {
            SelectedRooms = [0],
            RoleOptions = new Dictionary<string, string> { ["receptacle"] = "t2", ["luminaire"] = "t3" },
            HeightMillimetres = new Dictionary<string, int> { ["receptacle"] = 300, ["luminaire"] = 2400 },
        };
        Assert.True(configuration.Matches(generation));
        Assert.False(configuration.LinkedBoundaryConflict(generation));
        Assert.True(PatternTypeCompatibility.LinkedBoundary("work_plane_based"));
        Assert.False(PatternTypeCompatibility.LinkedBoundary("one_level_based_hosted"));
        Assert.True(PatternTypeCompatibility.LinkedBoundary("one_level_based"));
    }

    [Fact]
    public async Task An_omitted_role_places_nothing_and_needs_no_height()
    {
        var (store, preview) = Setup();
        var configuration = JsonSerializer.Serialize(new
        {
            rooms = new[] { 0 },
            roles = new object[]
            {
                new { role = "receptacle", option = "t1", height_mm = 300 },
                new { role = "luminaire", option = PatternLocalConfiguration.OmitOption },
            },
        });

        var outcome = await Orchestrator(store, preview).PrepareAsync(configuration, CancellationToken.None);

        Assert.False(outcome.IsError);
        var placement = Assert.Single(Assert.Single(preview.Requests).Placements!);
        Assert.Equal("receptacle", placement.Role);
        Assert.Equal("type-face", placement.FamilyTypeRef);
    }

    [Fact]
    public async Task A_work_plane_interior_type_cannot_be_raised_above_the_level()
    {
        var (store, preview) = Setup();
        string Configuration(int height) => JsonSerializer.Serialize(new
        {
            rooms = new[] { 0 },
            roles = new object[]
            {
                new { role = "receptacle", option = PatternLocalConfiguration.OmitOption },
                new { role = "luminaire", option = "t4", height_mm = height },
            },
        });

        var raised = await Orchestrator(store, preview).PrepareAsync(Configuration(2400), CancellationToken.None);
        Assert.True(raised.IsError);
        Assert.Contains("stays on the Level plane", raised.Body, StringComparison.Ordinal);
        Assert.Empty(preview.Requests);

        var onLevel = await Orchestrator(store, preview).PrepareAsync(Configuration(0), CancellationToken.None);
        Assert.False(onLevel.IsError);
        Assert.Equal("type-recessed", Assert.Single(Assert.Single(preview.Requests).Placements!).FamilyTypeRef);
    }

    [Fact]
    public async Task At_least_one_role_must_be_placed()
    {
        var (store, preview) = Setup();
        var configuration = JsonSerializer.Serialize(new
        {
            rooms = new[] { 0 },
            roles = new object[]
            {
                new { role = "receptacle", option = PatternLocalConfiguration.OmitOption },
                new { role = "luminaire", option = PatternLocalConfiguration.OmitOption },
            },
        });

        var outcome = await Orchestrator(store, preview).PrepareAsync(configuration, CancellationToken.None);

        Assert.True(outcome.IsError);
        Assert.Contains("At least one role", outcome.Body, StringComparison.Ordinal);
        Assert.Empty(preview.Requests);
    }

    [Theory]
    [InlineData(PreviewRoomPatternItemStatus.HostLevelNotFound, "No host level")]
    [InlineData(PreviewRoomPatternItemStatus.HostLevelAmbiguous, "Several host levels")]
    [InlineData(PreviewRoomPatternItemStatus.LinkedRoomUnavailable, "no longer available")]
    [InlineData(PreviewRoomPatternItemStatus.LinkedHostRequiresWorkPlaneBased, "work-plane-based")]
    [InlineData(PreviewRoomPatternItemStatus.LinkedBoundaryUnsupported, "not supported")]
    public async Task Linked_preview_failures_are_explained_without_identities(PreviewRoomPatternItemStatus status, string expected)
    {
        var (store, preview) = Setup();
        preview.Status = status;
        var outcome = await Orchestrator(store, preview).PrepareAsync(Configuration(wall: "t1"), CancellationToken.None);

        Assert.True(outcome.IsError);
        Assert.Contains(expected, outcome.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("sr1.", outcome.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("unique", outcome.Body, StringComparison.Ordinal);
    }

    private static string Configuration(string wall)
    {
        return JsonSerializer.Serialize(new
        {
            rooms = new[] { 0 },
            roles = new object[]
            {
                new { role = "receptacle", option = wall, height_mm = 300 },
                new { role = "luminaire", option = "t3", height_mm = 2400 },
            },
        });
    }

    private static (RoomPatternStore Store, FakePreview Preview) Setup()
    {
        var store = new RoomPatternStore();
        var generation = Generation(LinkedRoom);
        store.Replace(new RoomPatternRecord("artifact", 1, 2), null, generation);
        return (store, new FakePreview());
    }

    private static RoomPatternGeneration Generation(string roomRef, string band = "high")
    {
        return new RoomPatternGeneration
        {
            GenerationId = "0123456789abcdef0123456789abcdef",
            InstanceId = Instance,
            DocumentId = "document",
            ArtifactId = "artifact",
            PageNumber = 1,
            Source = new SourceRoomPattern(
                [new PlanPoint(0, 0), new PlanPoint(10, 0), new PlanPoint(10, 6), new PlanPoint(0, 6)],
                null,
                [new SourceDevice("receptacle", 5, 0.2, "boundary", null), new SourceDevice("luminaire", 5, 3, "interior", null)]),
            Rooms =
            [
                new HiddenPatternRoom
                {
                    Index = 0,
                    RoomRef = roomRef,
                    Label = "Studio Unit 203",
                    Band = band,
                    Score = 0.9,
                    RotationDegrees = 0,
                    Mirrored = false,
                    AdaptationStatus = "resolved",
                    Devices =
                    [
                        new HiddenPatternDevice { Role = "receptacle", PlacementKind = "boundary", XMetres = 1, YMetres = 0.15, QuarterTurns = 0 },
                        new HiddenPatternDevice { Role = "luminaire", PlacementKind = "interior", XMetres = 2, YMetres = 2, QuarterTurns = 0 },
                    ],
                },
            ],
            Options = new Dictionary<string, LoadedMepTypeOption>
            {
                ["t1"] = new("t1", "type-face", "OST_ElectricalFixtures", "Electrical Fixtures", "Receptacle", "Face", "work_plane_based", true),
                ["t2"] = new("t2", "type-wall", "OST_ElectricalFixtures", "Electrical Fixtures", "Receptacle", "Wall", "one_level_based_hosted", true),
                ["t3"] = new("t3", "type-light", "OST_LightingFixtures", "Lighting Fixtures", "Light", "Ceiling", "one_level_based", true),
                ["t4"] = new("t4", "type-recessed", "OST_LightingFixtures", "Lighting Fixtures", "Recessed", "Downlight", "work_plane_based", true),
            },
        };
    }

    private static RoomPatternPlacementOrchestrator Orchestrator(RoomPatternStore store, FakePreview preview)
    {
        return new RoomPatternPlacementOrchestrator(preview, new NoReview(), new NoApply(), store, Instance);
    }

    private sealed class FakePreview : IRoomPatternPreviewClient
    {
        public List<PreviewRoomPatternPlacementsRequest> Requests { get; } = [];

        public PreviewRoomPatternItemStatus Status { get; set; } = PreviewRoomPatternItemStatus.Ok;

        public Task<AssistantToolCall> PreviewAsync(string instanceId, PreviewRoomPatternPlacementsRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var ready = Status == PreviewRoomPatternItemStatus.Ok;
            var result = new PreviewRoomPatternPlacementsResult
            {
                Context = new PreviewRoomPatternPlacementsContext { InstanceId = instanceId, DocumentId = "document" },
                LengthUnit = PreviewRoomPatternPlacementsResult.Metres,
                Ready = ready,
                RoomCount = 1,
                Items = request.Placements!.Select((_, index) => new PreviewRoomPatternItemResult { Position = index + 1, Status = Status }).ToArray(),
                IntentRef = ready ? "intent-ref" : null,
                IntentFingerprint = ready ? new string('a', 64) : null,
                ExpiresAt = ready ? DateTimeOffset.UnixEpoch.AddMinutes(10) : null,
            };
            return Task.FromResult(new AssistantToolCall(false, null, null, JsonSerializer.SerializeToElement(result, ContractJson.Options)));
        }
    }

    private sealed class NoReview : IMepDevicePlacementReviewClient
    {
        public Task<AssistantToolCall> ReviewAsync(string instanceId, string intentRef, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Prepare must not review.");
    }

    private sealed class NoApply : IMepDevicePlacementApplyClient
    {
        public Task<AssistantToolCall> ApplyAsync(string instanceId, string intentRef, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Prepare must not apply.");
    }
}
