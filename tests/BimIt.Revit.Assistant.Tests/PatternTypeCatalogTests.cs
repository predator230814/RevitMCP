using System.Text.Json;
using BimIt.Revit.Assistant.Patterns;
using BimIt.Revit.Contracts;
using Xunit;

namespace BimIt.Revit.Assistant.Tests;

public sealed class PatternTypeCatalogTests
{
    [Fact]
    public void Roles_are_grouped_by_their_preferred_categories()
    {
        var groups = PatternTypeCatalog.CategoryGroups(["receptacle", "luminaire", "receptacle", "other"]);
        Assert.Equal(3, groups.Count);
        Assert.Contains(groups, group => group is not null && group.SequenceEqual(["OST_LightingFixtures"]));
        Assert.Contains(groups, group => group is not null && group.SequenceEqual(["OST_ElectricalFixtures"]));
        Assert.Contains(groups, group => group is null);
    }

    [Fact]
    public async Task Each_role_category_is_read_on_its_own_so_other_categories_cannot_crowd_it_out()
    {
        var session = new CatalogSession();
        var options = await PatternTypeCatalog.LoadAsync(session, "instance", "document", ["receptacle", "luminaire"], CancellationToken.None);

        Assert.Equal(2, session.Calls.Count);
        Assert.All(session.Calls, call => Assert.Equal(80, call["limit"]));
        Assert.Contains(session.Calls, call => ((IReadOnlyList<string>)call["category_tokens"]!).SequenceEqual(["OST_ElectricalFixtures"]));
        Assert.Contains(session.Calls, call => ((IReadOnlyList<string>)call["category_tokens"]!).SequenceEqual(["OST_LightingFixtures"]));
        Assert.Contains(options, option => option.FamilyName == "Duplex Receptacle" && option.PlacementClassification == "work_plane_based");
        Assert.Contains(options, option => option.CategoryToken == "OST_LightingFixtures");
        Assert.Equal(options.Count, options.Select(option => option.OptionKey).Distinct().Count());
        Assert.Equal(options.Count, options.Select(option => option.FamilyTypeRef).Distinct().Count());
    }

    [Fact]
    public async Task Search_reaches_a_type_beyond_the_first_catalog_page_and_adds_it_to_the_generation()
    {
        var store = new RoomPatternStore();
        store.Replace(new RoomPatternRecord("artifact", 1, 1), null, Generation());
        var session = new SearchSession(Enumerable.Range(0, 3).Select(index => Item("tt-" + index, "OST_ElectricalFixtures", "Tt_INS_Prise", "15A_" + index, "one_level_based")).ToArray());
        var service = new RoomPatternTypeSearchService(session, store, "instance");

        var outcome = await service.SearchAsync(BimIt.Revit.Assistant.Ipc.RoomPatternTypeSearch.SerializeRequest("receptacle", " Tt_INS "), CancellationToken.None);

        Assert.False(outcome.IsError);
        var call = Assert.Single(session.Calls);
        Assert.Equal("Tt_INS", call["search"]);
        Assert.Equal(80, call["limit"]);
        Assert.Equal(["OST_ElectricalFixtures"], (IReadOnlyList<string>)call["category_tokens"]!);
        Assert.True(BimIt.Revit.Assistant.Ipc.RoomPatternTypeSearch.TryParseResult(outcome.ResultJson, out var result));
        var keys = result.GetProperty("options").EnumerateArray().Select(option => option.GetProperty("key").GetString()!).ToArray();
        Assert.Equal(["t1", "t2", "t3"], keys);
        Assert.DoesNotContain("tt-0", outcome.ResultJson!, StringComparison.Ordinal);
        var generation = store.CurrentGeneration!;
        Assert.Equal(4, generation.Options.Count);
        Assert.Equal("tt-2", generation.Options["t3"].FamilyTypeRef);
        Assert.Equal("one_level_based", generation.Options["t3"].PlacementClassification);
    }

    [Fact]
    public async Task Search_again_keeps_the_keys_already_offered()
    {
        var store = new RoomPatternStore();
        store.Replace(new RoomPatternRecord("artifact", 1, 1), null, Generation());
        var session = new SearchSession([Item("existing", "OST_ElectricalFixtures", "Duplex Receptacle", "Standard", "work_plane_based"), Item("tt", "OST_ElectricalFixtures", "Tt_INS_Prise", "15A", "one_level_based")]);
        var service = new RoomPatternTypeSearchService(session, store, "instance");
        var request = BimIt.Revit.Assistant.Ipc.RoomPatternTypeSearch.SerializeRequest("receptacle", "e");

        await service.SearchAsync(request, CancellationToken.None);
        var second = await service.SearchAsync(request, CancellationToken.None);

        Assert.False(second.IsError);
        var generation = store.CurrentGeneration!;
        Assert.Equal(2, generation.Options.Count);
        Assert.Equal("existing", generation.Options["t0"].FamilyTypeRef);
        Assert.Equal("tt", generation.Options["t1"].FamilyTypeRef);
    }

    [Fact]
    public async Task Search_is_refused_after_the_pattern_was_applied_and_for_an_invalid_request()
    {
        var store = new RoomPatternStore();
        var generation = Generation();
        store.Replace(new RoomPatternRecord("artifact", 1, 1), null, generation);
        var session = new SearchSession([Item("tt", "OST_ElectricalFixtures", "Tt_INS_Prise", "15A", "one_level_based")]);
        var service = new RoomPatternTypeSearchService(session, store, "instance");

        var invalid = await service.SearchAsync("{\"role\":\"pump\",\"query\":\"Tt\"}", CancellationToken.None);
        Assert.True(invalid.IsError);
        Assert.Empty(session.Calls);

        store.RememberTerminal(generation.GenerationId, "Applied 2 pattern placements.", false);
        var applied = await service.SearchAsync(BimIt.Revit.Assistant.Ipc.RoomPatternTypeSearch.SerializeRequest("receptacle", "Tt"), CancellationToken.None);
        Assert.True(applied.IsError);
        Assert.Null(applied.ResultJson);
        Assert.Empty(session.Calls);
        Assert.Single(store.CurrentGeneration!.Options);
    }

    [Fact]
    public async Task A_truncated_search_asks_to_refine_it()
    {
        var store = new RoomPatternStore();
        store.Replace(new RoomPatternRecord("artifact", 1, 1), null, Generation());
        var session = new SearchSession([Item("tt", "OST_ElectricalFixtures", "Tt_INS_Prise", "15A", "one_level_based")], truncated: true);
        var outcome = await new RoomPatternTypeSearchService(session, store, "instance")
            .SearchAsync(BimIt.Revit.Assistant.Ipc.RoomPatternTypeSearch.SerializeRequest("receptacle", "Tt"), CancellationToken.None);

        Assert.False(outcome.IsError);
        Assert.Contains("Refine the search", outcome.Body, StringComparison.Ordinal);
        Assert.True(BimIt.Revit.Assistant.Ipc.RoomPatternTypeSearch.TryParseResult(outcome.ResultJson, out var result));
        Assert.True(result.GetProperty("truncated").GetBoolean());
    }

    [Fact]
    public void A_found_option_can_be_configured_for_a_linked_wall()
    {
        var store = new RoomPatternStore();
        store.Replace(new RoomPatternRecord("artifact", 1, 1), null, Generation());
        Assert.True(store.TryAddOptions(store.CurrentGeneration!.GenerationId, [new LoadedMepTypeItem("tt", "OST_ElectricalFixtures", "Electrical Fixtures", "Tt_INS_Prise", "15A", "one_level_based", false)], out var added));

        var configuration = new PatternLocalConfiguration
        {
            SelectedRooms = [0],
            RoleOptions = new Dictionary<string, string> { ["receptacle"] = added[0].OptionKey },
            HeightMillimetres = new Dictionary<string, int> { ["receptacle"] = 300 },
        };
        Assert.True(store.TryConfigure(configuration));
    }

    private static RoomPatternGeneration Generation()
    {
        return new RoomPatternGeneration
        {
            GenerationId = "0123456789abcdef0123456789abcdef",
            InstanceId = "instance",
            DocumentId = "document",
            ArtifactId = "artifact",
            PageNumber = 1,
            Source = new SourceRoomPattern(
                [new PlanPoint(0, 0), new PlanPoint(4, 0), new PlanPoint(4, 3), new PlanPoint(0, 3)],
                null,
                [new SourceDevice("receptacle", 2, 0.2, "boundary", null)]),
            Rooms =
            [
                new HiddenPatternRoom
                {
                    Index = 0,
                    RoomRef = BimIt.Revit.Contracts.SpatialRoomRefCodec.Link("link-unique", "room-unique"),
                    Label = "Studio Unit 203",
                    Band = "high",
                    Score = 0.9,
                    RotationDegrees = 0,
                    Mirrored = false,
                    AdaptationStatus = "resolved",
                    Devices = [new HiddenPatternDevice { Role = "receptacle", PlacementKind = "boundary", XMetres = 2, YMetres = 0.15, QuarterTurns = 0 }],
                },
            ],
            Options = new Dictionary<string, LoadedMepTypeOption>
            {
                ["t0"] = new("t0", "existing", "OST_ElectricalFixtures", "Electrical Fixtures", "Duplex Receptacle", "Standard", "work_plane_based", true),
            },
        };
    }

    private static MepDeviceTypeItem Item(string reference, string category, string family, string type, string placement)
    {
        return new MepDeviceTypeItem
        {
            FamilyTypeRef = reference,
            CategoryToken = category,
            CategoryName = "Electrical Fixtures",
            FamilyName = family,
            TypeName = type,
            PlacementClassification = placement,
            Active = false,
        };
    }

    private sealed class SearchSession(MepDeviceTypeItem[] items, bool truncated = false) : IAssistantToolSession
    {
        public List<IReadOnlyDictionary<string, object?>> Calls { get; } = [];

        public Task<AssistantMcpReadiness> InitializeAsync(CancellationToken cancellationToken)
            => Task.FromResult(new AssistantMcpReadiness(true, 18, [], null));

        public Task<AssistantToolCall> CallAsync(string toolName, IReadOnlyDictionary<string, object?> arguments, CancellationToken cancellationToken)
        {
            Assert.Equal(PatternTypeCatalog.ToolName, toolName);
            Calls.Add(arguments);
            var result = new GetMepDeviceTypesResult
            {
                Context = new GetMepDeviceTypesContext { InstanceId = "instance", DocumentId = "document" },
                Truncated = truncated,
                Items = items,
            };
            return Task.FromResult(new AssistantToolCall(false, null, null, JsonSerializer.SerializeToElement(result, ContractJson.Options)));
        }
    }

    private sealed class CatalogSession : IAssistantToolSession
    {
        public List<IReadOnlyDictionary<string, object?>> Calls { get; } = [];

        public Task<AssistantMcpReadiness> InitializeAsync(CancellationToken cancellationToken)
            => Task.FromResult(new AssistantMcpReadiness(true, 18, [], null));

        public Task<AssistantToolCall> CallAsync(string toolName, IReadOnlyDictionary<string, object?> arguments, CancellationToken cancellationToken)
        {
            Assert.Equal(PatternTypeCatalog.ToolName, toolName);
            Calls.Add(arguments);
            var category = ((IReadOnlyList<string>)arguments["category_tokens"]!)[0];
            var items = category == "OST_ElectricalFixtures"
                ? new[] { Item("duplex", category, "Duplex Receptacle", "work_plane_based"), Item("wall", category, "Tt Prise", "one_level_based") }
                : new[] { Item("light", category, "Downlight", "work_plane_based"), Item("light", category, "Downlight", "work_plane_based") };
            var result = new GetMepDeviceTypesResult
            {
                Context = new GetMepDeviceTypesContext { InstanceId = "instance", DocumentId = "document" },
                Truncated = false,
                Items = items,
            };
            return Task.FromResult(new AssistantToolCall(false, null, null, JsonSerializer.SerializeToElement(result, ContractJson.Options)));
        }

        private static MepDeviceTypeItem Item(string reference, string category, string family, string placement)
        {
            return new MepDeviceTypeItem
            {
                FamilyTypeRef = reference,
                CategoryToken = category,
                CategoryName = category,
                FamilyName = family,
                TypeName = "Standard",
                PlacementClassification = placement,
                Active = true,
            };
        }
    }
}
