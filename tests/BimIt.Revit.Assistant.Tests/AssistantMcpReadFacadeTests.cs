using System.Text.Json;
using BimIt.Revit.Assistant.Patterns;
using BimIt.Revit.Assistant.Providers.GitHubCopilot;
using Microsoft.Extensions.AI;
using Xunit;

namespace BimIt.Revit.Assistant.Tests;

public sealed class AssistantMcpReadFacadeTests
{
    private const string InstanceId = "revit-instance-trusted";

    private static readonly string[] TrustedWrites =
    [
        "revit_preview_parameter_updates",
        "revit_request_parameter_update_review",
        "revit_apply_parameter_updates",
        "revit_preview_mep_device_placements",
        "revit_request_mep_device_placement_review",
        "revit_apply_mep_device_placements",
        "revit_preview_room_pattern_placements",
    ];

    [Fact]
    public void Every_current_server_read_is_discovered_from_metadata_and_no_write_is()
    {
        var descriptors = ServerToolMetadata.Descriptors;
        Assert.Equal(18, descriptors.Count);
        var facade = Facade(new ScriptedSession(), descriptors);

        Assert.Equal(AssistantAgentTests.ExpectedGenericReads, facade.Tools.Select(tool => tool.Name).ToArray());
        Assert.Contains("revit_read_spatial_rooms", facade.Tools.Select(tool => tool.Name));
        Assert.Contains("revit_describe_parameters", facade.Tools.Select(tool => tool.Name));
        Assert.Contains("revit_get_parameter_values", facade.Tools.Select(tool => tool.Name));
        foreach (var write in TrustedWrites)
        {
            Assert.Contains(write, descriptors.Select(tool => tool.Name));
            Assert.False(facade.IsExposed(write));
        }

        // The discovered reads are exactly the Server's read-only, closed-world, non-destructive tools.
        var serverReads = descriptors
            .Where(tool => tool.ReadOnlyHint == true && tool.DestructiveHint != true && tool.OpenWorldHint == false)
            .Select(tool => tool.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(serverReads, facade.Tools.Select(tool => tool.Name).ToArray());
        Assert.Equal(7, facade.Rejected.Count);
    }

    [Theory]
    [InlineData(null, false, false, "not_read_only")]
    [InlineData(false, false, false, "not_read_only")]
    [InlineData(true, true, false, "destructive")]
    [InlineData(true, false, null, "open_world")]
    [InlineData(true, false, true, "open_world")]
    public void Missing_or_unsafe_annotations_fail_closed(bool? readOnly, bool? destructive, bool? openWorld, string reason)
    {
        var descriptor = ServerToolMetadata.Get("revit_read_spatial_rooms") with
        {
            ReadOnlyHint = readOnly,
            DestructiveHint = destructive,
            OpenWorldHint = openWorld,
        };

        Assert.False(AssistantReadToolPolicy.TryCreate(descriptor, out var tool, out var actual));
        Assert.Null(tool);
        Assert.Equal(reason, actual);
    }

    [Fact]
    public void A_name_alone_never_qualifies_a_tool()
    {
        var read = ServerToolMetadata.Get("revit_read_spatial_rooms");
        Assert.False(AssistantReadToolPolicy.TryCreate(read with { Name = "revit_apply_parameter_updates" }, out _, out var write));
        Assert.Equal("trusted_write", write);
        Assert.False(AssistantReadToolPolicy.TryCreate(read with { Name = "other_read_rooms" }, out _, out var prefix));
        Assert.Equal("name", prefix);
        Assert.False(AssistantReadToolPolicy.TryCreate(read with { Name = "revit_future_tool", ReadOnlyHint = null }, out _, out _));
        Assert.False(AssistantReadToolPolicy.TryCreate(read with { Description = "" }, out _, out _));
        Assert.False(AssistantReadToolPolicy.TryCreate(read with { InputSchema = Json("""{"type":"object"}""") }, out _, out _));
        Assert.False(AssistantReadToolPolicy.TryCreate(
            read with { InputSchema = Json("""{"type":"object","properties":{"name_contains":{"type":"string"}}}""") },
            out _,
            out var binding));
        Assert.Equal("no_instance_binding", binding);
        Assert.True(AssistantReadToolPolicy.TryCreate(read with { Name = "revit_future_read" }, out _, out _));
    }

    [Fact]
    public void Provider_schema_drops_only_trusted_binding_and_keeps_required_semantics()
    {
        var facade = Facade(new ScriptedSession(), ServerToolMetadata.Descriptors);
        foreach (var tool in facade.Tools)
        {
            var schema = tool.ProviderSchema.GetRawText();
            Assert.DoesNotContain("instance_id", schema, StringComparison.Ordinal);
            Assert.DoesNotContain("document_id", schema, StringComparison.Ordinal);
        }

        var rooms = facade.Tools.Single(tool => tool.Name == "revit_read_spatial_rooms");
        Assert.True(rooms.InjectsDocument);
        Assert.True(rooms.RequiresDocument);
        Assert.Equal(["limit", "name_contains"], rooms.Properties.Order(StringComparer.Ordinal).ToArray());
        Assert.False(rooms.ProviderSchema.TryGetProperty("required", out _));
        Assert.False(rooms.ProviderSchema.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(100, rooms.ProviderSchema.GetProperty("properties").GetProperty("limit").GetProperty("maximum").GetInt32());

        var elements = facade.Tools.Single(tool => tool.Name == "revit_get_elements");
        Assert.True(elements.RequiresDocument);
        Assert.Equal(
            ["element_refs", "projection"],
            elements.ProviderSchema.GetProperty("required").EnumerateArray().Select(item => item.GetString()!).Order(StringComparer.Ordinal).ToArray());

        var context = facade.Tools.Single(tool => tool.Name == "revit_get_context");
        Assert.False(context.InjectsDocument);
        Assert.Empty(context.Properties);

        var server = ServerToolMetadata.Get("revit_get_elements").InputSchema;
        Assert.Contains("document_id", server.GetProperty("required").EnumerateArray().Select(item => item.GetString()));
    }

    [Fact]
    public async Task Read_spatial_rooms_gets_the_trusted_instance_and_fresh_active_document()
    {
        var session = new ScriptedSession();
        session.Document = "doc-electrical";
        var facade = Facade(session, ServerToolMetadata.Descriptors);

        var result = await facade.InvokeAsync("revit_read_spatial_rooms", Args("""{"name_contains":"Studio"}"""), CancellationToken.None);

        Assert.True((bool)result["ok"]!);
        Assert.Equal(["revit_get_context", "revit_read_spatial_rooms"], session.Calls.Select(call => call.Tool).ToArray());
        var call = session.Calls[1].Arguments;
        Assert.Equal(InstanceId, call["instance_id"]);
        Assert.Equal("doc-electrical", call["document_id"]);
        Assert.Equal("Studio", ((JsonElement)call["name_contains"]!).GetString());
        Assert.Equal(3, call.Count);
        Assert.Equal(["revit_read_spatial_rooms"], facade.ConsumeUsedTools());
    }

    [Fact]
    public async Task Active_document_change_between_calls_is_followed()
    {
        var session = new ScriptedSession { Document = "doc-a" };
        var facade = Facade(session, ServerToolMetadata.Descriptors);

        await facade.InvokeAsync("revit_get_warnings", null, CancellationToken.None);
        session.Document = "doc-b";
        await facade.InvokeAsync("revit_get_warnings", null, CancellationToken.None);

        var warnings = session.Calls.Where(call => call.Tool == "revit_get_warnings").ToArray();
        Assert.Equal("doc-a", warnings[0].Arguments["document_id"]);
        Assert.Equal("doc-b", warnings[1].Arguments["document_id"]);
    }

    [Fact]
    public async Task No_active_document_stops_a_read_that_requires_one()
    {
        var session = new ScriptedSession { Document = null };
        var facade = Facade(session, ServerToolMetadata.Descriptors);

        var result = await facade.InvokeAsync("revit_read_spatial_rooms", Args("""{"name_contains":"Studio"}"""), CancellationToken.None);

        Assert.False((bool)result["ok"]!);
        Assert.Equal("NO_ACTIVE_DOCUMENT", result["code"]);
        Assert.DoesNotContain(session.Calls, call => call.Tool == "revit_read_spatial_rooms");
    }

    [Theory]
    [InlineData("""{"document_id":"doc-forged","name_contains":"Studio"}""", "argument_not_allowed")]
    [InlineData("""{"instance_id":"other-revit"}""", "argument_not_allowed")]
    [InlineData("""{"script":"rm -rf"}""", "invalid_arguments")]
    public async Task Malformed_or_identity_arguments_are_rejected_before_mcp(string json, string code)
    {
        var session = new ScriptedSession { Document = "doc-electrical" };
        var facade = Facade(session, ServerToolMetadata.Descriptors);

        var result = await facade.InvokeAsync("revit_read_spatial_rooms", Args(json), CancellationToken.None);

        Assert.False((bool)result["ok"]!);
        Assert.Equal(code, result["code"]);
        Assert.Empty(session.Calls);
    }

    [Fact]
    public async Task Server_validation_errors_reach_the_model_unchanged()
    {
        var session = new ScriptedSession { Document = "doc-electrical", Fail = ("INVALID_REQUEST", "The tool request contains unexpected properties.") };
        var facade = Facade(session, ServerToolMetadata.Descriptors);

        var result = await facade.InvokeAsync("revit_read_spatial_rooms", Args("""{"limit":0}"""), CancellationToken.None);

        Assert.Equal("INVALID_REQUEST", result["code"]);
    }

    [Fact]
    public async Task A_ten_thousand_character_result_reaches_the_model_whole()
    {
        var rooms = string.Join(",", Enumerable.Range(0, 6).Select(index => "{\"name\":\"Studio " + index + "\",\"boundary\":\"" + new string('x', 1_700) + "\"}"));
        var session = new ScriptedSession { Document = "doc-electrical", Result = "{\"items\":[" + rooms + "],\"truncated\":false}" };
        var facade = Facade(session, ServerToolMetadata.Descriptors);

        var result = await facade.InvokeAsync("revit_read_spatial_rooms", Args("""{"name_contains":"Studio"}"""), CancellationToken.None);

        Assert.True((bool)result["ok"]!);
        var items = ((System.Text.Json.Nodes.JsonNode)result["result"]!)["items"]!.AsArray();
        Assert.Equal(6, items.Count);
    }

    [Fact]
    public async Task An_oversized_result_is_refused_explicitly_not_cut()
    {
        var session = new ScriptedSession
        {
            Document = "doc-electrical",
            Result = "{\"items\":[\"" + new string('x', AssistantMcpReadFacade.MaxResultCharacters) + "\"],\"truncated\":false}",
        };
        var facade = Facade(session, ServerToolMetadata.Descriptors);

        var result = await facade.InvokeAsync("revit_read_spatial_rooms", Args("""{"limit":100}"""), CancellationToken.None);

        Assert.False((bool)result["ok"]!);
        Assert.Equal("result_too_large", result["code"]);
        Assert.Contains("smaller limit", (string)result["message"]!, StringComparison.Ordinal);
        Assert.False(result.ContainsKey("result"));
    }

    [Fact]
    public async Task A_tool_removed_from_the_server_is_not_exposed_and_cannot_be_called()
    {
        var withoutRooms = ServerToolMetadata.Descriptors.Where(tool => tool.Name != "revit_read_spatial_rooms").ToArray();
        var session = new ScriptedSession { Document = "doc-electrical" };
        var facade = Facade(session, withoutRooms);

        Assert.False(facade.IsExposed("revit_read_spatial_rooms"));
        var result = await facade.InvokeAsync("revit_read_spatial_rooms", null, CancellationToken.None);
        Assert.Equal("tool_not_allowed", result["code"]);
        Assert.Empty(session.Calls);
    }

    [Fact]
    public async Task Write_tools_cannot_be_invoked_through_the_generic_facade()
    {
        var session = new ScriptedSession { Document = "doc-electrical" };
        var facade = Facade(session, ServerToolMetadata.Descriptors);
        foreach (var write in TrustedWrites)
        {
            var result = await facade.InvokeAsync(write, Args("""{"intent_ref":"intent-1"}"""), CancellationToken.None);
            Assert.Equal("tool_not_allowed", result["code"]);
        }

        Assert.Empty(session.Calls);
    }

    [Fact]
    public async Task Copilot_function_carries_the_server_schema_and_calls_the_facade()
    {
        var session = new ScriptedSession { Document = "doc-electrical" };
        var facade = Facade(session, ServerToolMetadata.Descriptors);
        var tools = GitHubCopilotSessionBlueprint.CreateTools(
            facade,
            new MepDevicePlacementProposalTool(session, InstanceId, new MepDevicePlacementProposalStore(), static () => null),
            new RoomPatternProposalTool(session, InstanceId, new RoomPatternStore(), static () => null),
            new BimItSkillTool(() => null));
        var rooms = tools.Single(tool => tool.Name == "revit_read_spatial_rooms");
        Assert.Contains("name_contains", rooms.JsonSchema.GetRawText(), StringComparison.Ordinal);
        Assert.Equal(ServerToolMetadata.Get("revit_read_spatial_rooms").Description, rooms.Description);
        Assert.DoesNotContain(tools, tool => TrustedWrites.Contains(tool.Name));
        Assert.DoesNotContain(tools, tool => BimItAgentReadTools.Names.Contains(tool.Name));

        var result = await rooms.InvokeAsync(new AIFunctionArguments { ["name_contains"] = Json("\"Studio\"") });

        Assert.True(((JsonElement)result!).GetProperty("ok").GetBoolean());
        Assert.Equal("doc-electrical", session.Calls.Single(call => call.Tool == "revit_read_spatial_rooms").Arguments["document_id"]);
    }

    [Fact]
    public void Session_config_refuses_a_hand_made_read_or_a_write()
    {
        var directory = Directory.CreateTempSubdirectory("bimit-read-surface-").FullName;
        try
        {
            var session = new ScriptedSession();
            var tools = GitHubCopilotSessionBlueprint.CreateTools(
                Facade(session, ServerToolMetadata.Descriptors),
                new MepDevicePlacementProposalTool(session, InstanceId, new MepDevicePlacementProposalStore(), static () => null),
                new RoomPatternProposalTool(session, InstanceId, new RoomPatternStore(), static () => null),
                new BimItSkillTool(() => null)).ToList();
            var config = GitHubCopilotSessionBlueprint.CreateSessionConfig(directory, tools);

            // The model has no file tools, so a spilled temp-file reference would leave it with a partial answer.
            Assert.NotNull(config.LargeOutput);
            Assert.False(config.LargeOutput!.Enabled);

            var forged = AIFunctionFactory.Create(() => "x", "revit_apply_parameter_updates");
            Assert.Throws<ArgumentException>(() => GitHubCopilotSessionBlueprint.CreateSessionConfig(directory, [.. tools, forged]));
            var handmade = AIFunctionFactory.Create(() => "x", "revit_get_everything");
            Assert.Throws<ArgumentException>(() => GitHubCopilotSessionBlueprint.CreateSessionConfig(directory, [.. tools, handmade]));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Shipped_model_query_skill_names_only_tools_the_assistant_now_exposes()
    {
        var skill = File.ReadAllText(Path.Combine(SkillRegistryTests.RepoRoot(), "skills", "built-in", "revit-model-query", "SKILL.md"));
        var facade = Facade(new ScriptedSession(), ServerToolMetadata.Descriptors);
        var named = System.Text.RegularExpressions.Regex.Matches(skill, "`(revit_[a-z_]+)`").Select(match => match.Groups[1].Value).Distinct().ToArray();
        Assert.Contains("revit_read_spatial_rooms", named);
        Assert.All(named, name => Assert.True(facade.IsExposed(name), name));
        Assert.DoesNotContain("bimit_", skill, StringComparison.Ordinal);
    }

    private static AssistantMcpReadFacade Facade(IAssistantToolSession session, IReadOnlyList<AssistantMcpToolDescriptor> descriptors)
        => new(session, InstanceId, descriptors);

    private static IReadOnlyDictionary<string, object?> Args(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateObject().ToDictionary(property => property.Name, property => (object?)property.Value.Clone());
    }

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private sealed class ScriptedSession : IAssistantToolSession
    {
        public string? Document { get; set; } = "doc-default";

        public (string Code, string Message)? Fail { get; set; }

        public List<(string Tool, IReadOnlyDictionary<string, object?> Arguments)> Calls { get; } = [];

        public Task<AssistantMcpReadiness> InitializeAsync(CancellationToken cancellationToken)
            => Task.FromResult(new AssistantMcpReadiness(true, 0, [], null));

        public Task<AssistantToolCall> CallAsync(string toolName, IReadOnlyDictionary<string, object?> arguments, CancellationToken cancellationToken)
        {
            Calls.Add((toolName, arguments));
            if (toolName == "revit_get_context")
            {
                var document = Document is null ? "null" : "{\"title\":\"Doc\",\"document_id\":\"" + Document + "\"}";
                return Task.FromResult(new AssistantToolCall(false, null, null, Json("{\"document\":" + document + ",\"active_view\":null}")));
            }

            if (Fail is var (code, message))
            {
                return Task.FromResult(new AssistantToolCall(true, code, message, null));
            }

            return Task.FromResult(new AssistantToolCall(false, null, null, Json(Result)));
        }

        public string Result { get; set; } = "{\"items\":[],\"truncated\":false}";
    }
}
