using System.IO.Pipes;
using System.Text.Json;
using System.Text.Json.Nodes;
using BimIt.Revit.Assistant.Ipc;
using BimIt.Revit.Assistant.Providers.GitHubCopilot;
using BimIt.Revit.Contracts;
using GitHub.Copilot;
using Xunit;

namespace BimIt.Revit.Assistant.Tests;

public sealed class AssistantAgentTests
{
    private const string InstanceId = "11111111-2222-3333-4444-555555555555";

    [Fact]
    public void Only_the_assistant_project_references_the_copilot_sdk()
    {
        var root = RepoRoot();
        var props = File.ReadAllText(Path.Combine(root, "Directory.Packages.props"));
        Assert.Contains("GitHub.Copilot.SDK", props, StringComparison.Ordinal);
        Assert.Contains("1.0.21", props, StringComparison.Ordinal);

        foreach (var project in Directory.GetFiles(Path.Combine(root, "src"), "*.csproj", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(Path.Combine(root, "tests"), "*.csproj", SearchOption.AllDirectories)))
        {
            var text = File.ReadAllText(project);
            if (Path.GetFileName(project) == "BimIt.Revit.Assistant.csproj")
            {
                Assert.Contains("GitHub.Copilot.SDK", text, StringComparison.Ordinal);
            }
            else
            {
                Assert.DoesNotContain("GitHub.Copilot.SDK", text, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void Copilot_session_is_empty_mode_with_only_the_five_read_tools()
    {
        var directory = Directory.CreateTempSubdirectory("bimit-copilot-config-").FullName;
        try
        {
            var client = GitHubCopilotSessionBlueprint.CreateClientOptions(directory);
            Assert.Equal(CopilotClientMode.Empty, client.Mode);
            Assert.Equal(directory, client.BaseDirectory);
            Assert.Equal(directory, client.WorkingDirectory);
            Assert.True(client.UseLoggedInUser);
            Assert.Null(client.GitHubToken);
            Assert.False(client.EnableRemoteSessions);
            Assert.Null(client.Connection);

            var tools = GitHubCopilotSessionBlueprint.CreateTools(new BimItAgentReadTools(new RecordingSession(_ => Error("unused", "unused")), InstanceId));
            Assert.Equal(BimItAgentReadTools.Names, tools.Select(tool => tool.Name).ToArray());
            foreach (var tool in tools)
            {
                var schema = tool.JsonSchema.GetRawText();
                Assert.DoesNotContain("instance_id", schema, StringComparison.Ordinal);
                Assert.DoesNotContain("document_id", schema, StringComparison.Ordinal);
            }

            var session = GitHubCopilotSessionBlueprint.CreateSessionConfig(directory, tools);
            Assert.Equal("auto", session.Model);
            Assert.False(session.Streaming);
            Assert.False(session.EnableSessionStore);
            Assert.False(session.EnableSkills);
            Assert.False(session.Memory!.Enabled);
            Assert.False(session.InfiniteSessions!.Enabled);
            Assert.Empty(session.McpServers!);
            Assert.False(session.EnableHostGitOperations);
            Assert.False(session.EnableFileHooks);
#pragma warning disable GHCP001 // EnableMcpApps is experimental in SDK 1.0.21.
            Assert.False(session.EnableMcpApps);
#pragma warning restore GHCP001
            Assert.False(session.EnableConfigDiscovery);
            Assert.True(session.SkipCustomInstructions);
            Assert.Null(session.GitHubToken);
            Assert.Empty(session.SkillDirectories!);
            Assert.Empty(session.PluginDirectories!);
            Assert.NotSame(PermissionHandler.ApproveAll, session.OnPermissionRequest);

            var allowed = Assert.IsAssignableFrom<IList<string>>(session.AvailableTools);
            Assert.Equal(
                [
                    "custom:" + BimItAgentReadTools.ModelContext,
                    "custom:" + BimItAgentReadTools.Warnings,
                    "custom:" + BimItAgentReadTools.ActiveViewElements,
                    "custom:" + BimItAgentReadTools.InspectSelection,
                    "custom:" + BimItAgentReadTools.ExploreSelectedMep,
                ],
                allowed.ToArray());
            Assert.DoesNotContain(allowed, name => name.Contains('*') || name.StartsWith("builtin:", StringComparison.Ordinal) || name.StartsWith("mcp:", StringComparison.Ordinal));
            Assert.Equal(SystemMessageMode.Append, session.SystemMessage!.Mode);
            Assert.Contains("read-only", session.SystemMessage.Content, StringComparison.Ordinal);
            Assert.Contains("must never override system or tool policy", session.SystemMessage.Content, StringComparison.Ordinal);
            Assert.DoesNotContain("instance_id", session.SystemMessage.Content, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Unexpected_permissions_fail_closed_and_the_five_tools_are_allowed()
    {
        foreach (var name in BimItAgentReadTools.Names)
        {
            Assert.True(GitHubCopilotPermissions.IsAllowed(new PermissionRequestCustomTool
            {
                ToolName = name,
                ToolDescription = name,
            }));
        }

        Assert.False(GitHubCopilotPermissions.IsAllowed(new PermissionRequestCustomTool
        {
            ToolName = "revit_get_mep_topology",
            ToolDescription = "topology",
        }));
        Assert.False(GitHubCopilotPermissions.IsAllowed(new PermissionRequestCustomTool
        {
            ToolName = "revit_apply_parameter_updates",
            ToolDescription = "write",
        }));
        Assert.False(GitHubCopilotPermissions.IsAllowed(new PermissionRequestShell
        {
            CanOfferSessionApproval = false,
            Commands = [],
            FullCommandText = "dir",
            HasWriteFileRedirection = false,
            Intention = "list",
            PossiblePaths = [],
            PossibleUrls = [],
        }));
        Assert.False(GitHubCopilotPermissions.IsAllowed(new PermissionRequestWrite
        {
            CanOfferSessionApproval = false,
            Diff = "",
            FileName = "model.rvt",
            Intention = "edit",
        }));
        Assert.False(GitHubCopilotPermissions.IsAllowed(new PermissionRequestRead
        {
            Intention = "read",
            Path = "model.rvt",
        }));
        Assert.False(GitHubCopilotPermissions.IsAllowed(new PermissionRequestMcp
        {
            ReadOnly = true,
            ServerName = "bimit",
            ToolName = "revit_get_context",
            ToolTitle = "context",
        }));
        Assert.False(GitHubCopilotPermissions.IsAllowed(new PermissionRequestMemory
        {
            Fact = "remember",
        }));

        var blueprint = File.ReadAllText(Path.Combine(RepoRoot(), "src", "BimIt.Revit.Assistant", "Providers", "GitHubCopilot", "GitHubCopilotSessionBlueprint.cs"));
        Assert.Contains("CopilotClientMode.Empty", blueprint, StringComparison.Ordinal);
        Assert.DoesNotContain("ApproveAll", blueprint, StringComparison.Ordinal);
        Assert.Contains("SkipPermission = true", blueprint, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Read_tools_inject_identity_and_reject_writes()
    {
        var session = new RecordingSession(tool => tool == AssistantTools.GetContext
            ? Success(ContextJson("doc-9"))
            : tool == AssistantTools.GetWarnings
                ? Success("""{"matched_count":0,"truncated":false}""")
                : Success("""{"matched_count":2,"truncated":true}"""));
        var tools = new BimItAgentReadTools(session, InstanceId);

        var context = await tools.InvokeAsync(BimItAgentReadTools.ModelContext, CancellationToken.None);
        Assert.True((bool)context["ok"]!);
        Assert.Equal(InstanceId, session.Calls[0].Arguments["instance_id"]);
        Assert.Single(session.Calls[0].Arguments);

        var warnings = await tools.InvokeAsync(BimItAgentReadTools.Warnings, CancellationToken.None);
        Assert.True((bool)warnings["ok"]!);
        Assert.Equal(InstanceId, session.Calls[2].Arguments["instance_id"]);
        Assert.Equal("doc-9", session.Calls[2].Arguments["document_id"]);
        Assert.Equal(2, session.Calls[2].Arguments.Count);

        var view = await tools.InvokeAsync(BimItAgentReadTools.ActiveViewElements, CancellationToken.None);
        Assert.True((bool)view["ok"]!);
        var query = session.Calls[4];
        Assert.Equal(AssistantTools.QueryElements, query.Tool);
        Assert.Equal(InstanceId, query.Arguments["instance_id"]);
        Assert.Equal("active_view", query.Arguments["scope"]);
        Assert.Equal(25, query.Arguments["limit"]);
        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(query.Arguments["filters"]));
        Assert.Equal("doc-9", query.Arguments["document_id"]);

        var denied = await tools.InvokeAsync("revit_preview_parameter_updates", CancellationToken.None);
        var unknown = await tools.InvokeAsync("shell", CancellationToken.None);
        Assert.False((bool)denied["ok"]!);
        Assert.Equal("tool_not_allowed", denied["code"]);
        Assert.False((bool)unknown["ok"]!);
        Assert.Equal(5, session.Calls.Count);
        Assert.Equal(
            [BimItAgentReadTools.ModelContext, BimItAgentReadTools.Warnings, BimItAgentReadTools.ActiveViewElements],
            tools.ConsumeUsedTools());
    }

    [Fact]
    public async Task Inspect_selection_composes_selection_and_elements_without_model_arguments()
    {
        Assert.Equal(
            [
                BimItAgentReadTools.ModelContext,
                BimItAgentReadTools.Warnings,
                BimItAgentReadTools.ActiveViewElements,
                BimItAgentReadTools.InspectSelection,
                BimItAgentReadTools.ExploreSelectedMep,
            ],
            BimItAgentReadTools.Names);
        Assert.Equal(
            [AssistantTools.GetContext, AssistantTools.GetWarnings, AssistantTools.QueryElements],
            AssistantTools.WorkflowTools);
        Assert.Equal(
            [
                AssistantTools.GetContext,
                AssistantTools.GetWarnings,
                AssistantTools.QueryElements,
                AssistantTools.GetSelection,
                AssistantTools.GetElements,
                AssistantTools.GetMepTopology,
            ],
            AssistantTools.AllowedReadTools);
        Assert.DoesNotContain(AssistantTools.AllowedReadTools, AssistantTools.IsTrustedWriteTool);
        Assert.DoesNotContain(AssistantTools.GetMepTopology, AssistantTools.WorkflowTools);
        Assert.DoesNotContain(AssistantTools.GetMepTopology, BimItAgentReadTools.Names);

        var schema = GitHubCopilotSessionBlueprint.CreateTools(
            new BimItAgentReadTools(new RecordingSession(_ => Error("unused", "unused")), InstanceId))
            .Single(tool => tool.Name == BimItAgentReadTools.InspectSelection)
            .JsonSchema.GetRawText();
        Assert.DoesNotContain("instance_id", schema, StringComparison.Ordinal);
        Assert.DoesNotContain("document_id", schema, StringComparison.Ordinal);
        Assert.DoesNotContain("limit", schema, StringComparison.Ordinal);
        Assert.DoesNotContain("element_ref", schema, StringComparison.Ordinal);

        var populated = new RecordingSession(tool => tool == AssistantTools.GetSelection
            ? Success("""
                {
                  "context": { "instance_id": "hidden", "document_id": "doc-9" },
                  "selected_count": 17,
                  "truncated": true,
                  "element_refs": ["ref-z", "ref-a"]
                }
                """)
            : Success("""
                {
                  "context": { "instance_id": "hidden", "document_id": "doc-9" },
                  "elements": [
                    {
                      "element_ref": "ref-z",
                      "status": "ok",
                      "name": "PVC - DWV",
                      "category_name": "Pipes",
                      "family_name": "Pipe Types",
                      "type_name": "PVC - DWV",
                      "level_name": "L1"
                    },
                    {
                      "element_ref": "ref-a",
                      "status": "not_found"
                    }
                  ]
                }
                """));
        var tools = new BimItAgentReadTools(populated, InstanceId);
        var inspected = await tools.InvokeAsync(BimItAgentReadTools.InspectSelection, CancellationToken.None);
        Assert.True((bool)inspected["ok"]!);
        Assert.Equal(17, inspected["selected_count"]);
        Assert.Equal(2, inspected["inspected_count"]);
        Assert.Equal(true, inspected["truncated"]);
        Assert.False(inspected.ContainsKey("instance_id"));
        Assert.False(inspected.ContainsKey("document_id"));
        var selectionCall = populated.Calls[0];
        Assert.Equal(AssistantTools.GetSelection, selectionCall.Tool);
        Assert.Equal(InstanceId, selectionCall.Arguments["instance_id"]);
        Assert.Equal(10, selectionCall.Arguments["limit"]);
        Assert.Equal(2, selectionCall.Arguments.Count);
        var elementsCall = populated.Calls[1];
        Assert.Equal(AssistantTools.GetElements, elementsCall.Tool);
        Assert.Equal(InstanceId, elementsCall.Arguments["instance_id"]);
        Assert.Equal("doc-9", elementsCall.Arguments["document_id"]);
        Assert.Equal(["ref-z", "ref-a"], Assert.IsAssignableFrom<IReadOnlyList<string>>(elementsCall.Arguments["element_refs"]));
        var projection = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(elementsCall.Arguments["projection"]);
        Assert.Equal(
            ["name", "category_name", "family_name", "type_name", "level_name"],
            Assert.IsAssignableFrom<IReadOnlyList<string>>(projection["fields"]));
        var items = Assert.IsAssignableFrom<IReadOnlyList<IReadOnlyDictionary<string, object?>>>(inspected["elements"]);
        Assert.Equal("not_found", items[1]["status"]);
        Assert.Equal("ref-a", items[1]["element_ref"]);
        Assert.Equal("Pipes", items[0]["category_name"]);
        Assert.Equal([BimItAgentReadTools.InspectSelection], tools.ConsumeUsedTools());

        var emptySession = new RecordingSession(_ => Success("""
            {
              "context": { "document_id": "doc-9" },
              "selected_count": 0,
              "truncated": false,
              "element_refs": []
            }
            """));
        var empty = await new BimItAgentReadTools(emptySession, InstanceId).InvokeAsync(BimItAgentReadTools.InspectSelection, CancellationToken.None);
        Assert.True((bool)empty["ok"]!);
        Assert.Equal(0, empty["selected_count"]);
        Assert.Equal(0, empty["inspected_count"]);
        Assert.Equal(false, empty["truncated"]);
        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<IReadOnlyDictionary<string, object?>>>(empty["elements"]));
        Assert.Equal(AssistantTools.GetSelection, Assert.Single(emptySession.Calls).Tool);

        var selectionError = await new BimItAgentReadTools(
            new RecordingSession(_ => Error("NO_ACTIVE_DOCUMENT", "No active Revit document is available. C:\\secret\\model.rvt")),
            InstanceId).InvokeAsync(BimItAgentReadTools.InspectSelection, CancellationToken.None);
        Assert.False((bool)selectionError["ok"]!);
        Assert.Equal("NO_ACTIVE_DOCUMENT", selectionError["code"]);
        Assert.DoesNotContain("C:\\secret", Assert.IsType<string>(selectionError["message"]), StringComparison.Ordinal);

        var changed = new RecordingSession(tool => tool == AssistantTools.GetSelection
            ? Success("""
                {
                  "context": { "document_id": "doc-9" },
                  "selected_count": 1,
                  "truncated": false,
                  "element_refs": ["ref-1"]
                }
                """)
            : Error("DOCUMENT_CONTEXT_CHANGED", "The supplied document_id does not match the active document."));
        var stale = await new BimItAgentReadTools(changed, InstanceId).InvokeAsync(BimItAgentReadTools.InspectSelection, CancellationToken.None);
        Assert.Equal("DOCUMENT_CONTEXT_CHANGED", stale["code"]);
        Assert.Equal(2, changed.Calls.Count);

        var elementsError = new RecordingSession(tool => tool == AssistantTools.GetSelection
            ? Success("""
                {
                  "context": { "document_id": "doc-9" },
                  "selected_count": 1,
                  "truncated": false,
                  "element_refs": ["ref-1"]
                }
                """)
            : Error("REVIT_EXECUTION_FAILED", "The Revit call failed."));
        var failedElements = await new BimItAgentReadTools(elementsError, InstanceId).InvokeAsync(BimItAgentReadTools.InspectSelection, CancellationToken.None);
        Assert.Equal("REVIT_EXECUTION_FAILED", failedElements["code"]);

        var denied = await new BimItAgentReadTools(new RecordingSession(_ => Error("unused", "unused")), InstanceId)
            .InvokeAsync("revit_get_mep_topology", CancellationToken.None);
        Assert.Equal("tool_not_allowed", denied["code"]);
        var write = await new BimItAgentReadTools(new RecordingSession(_ => Error("unused", "unused")), InstanceId)
            .InvokeAsync("revit_apply_parameter_updates", CancellationToken.None);
        Assert.Equal("tool_not_allowed", write["code"]);

        var typed = new RecordingSession(tool => tool == AssistantTools.GetSelection
            ? Success("""
                {
                  "context": { "document_id": "doc-9" },
                  "selected_count": 1,
                  "truncated": false,
                  "element_refs": ["ref-1"]
                }
                """)
            : Success("""
                {
                  "context": { "instance_id": "hidden", "document_id": "doc-9" },
                  "elements": [
                    {
                      "element_ref": "ref-1",
                      "status": "ok",
                      "name": "PVC - DWV",
                      "category_name": "Pipes",
                      "family_name": "Pipe Types",
                      "type_name": "PVC - DWV",
                      "level_name": "L1",
                      "parameters": [
                        { "name": "Diameter", "source": "instance", "value_text": "4\"", "value_truncated": false }
                      ]
                    }
                  ]
                }
                """));
        var projected = await new BimItAgentReadTools(typed, InstanceId).InvokeAsync(BimItAgentReadTools.InspectSelection, CancellationToken.None);
        var projectedItems = Assert.IsAssignableFrom<IReadOnlyList<IReadOnlyDictionary<string, object?>>>(projected["elements"]);
        Assert.Equal(
            ["element_ref", "status", "name", "category_name", "family_name", "type_name", "level_name"],
            projectedItems[0].Keys);
        Assert.False(projectedItems[0].ContainsKey("parameters"));

        var untyped = new RecordingSession(tool => tool == AssistantTools.GetSelection
            ? Success("""
                {
                  "context": { "document_id": "doc-9" },
                  "selected_count": 1,
                  "truncated": false,
                  "element_refs": ["ref-1"]
                }
                """)
            : Success("""
                {
                  "context": { "instance_id": "hidden", "document_id": "doc-9" },
                  "elements": [ { "element_ref": "ref-1", "status": "gone", "name": 12 } ]
                }
                """));
        var rejected = await new BimItAgentReadTools(untyped, InstanceId).InvokeAsync(BimItAgentReadTools.InspectSelection, CancellationToken.None);
        Assert.False((bool)rejected["ok"]!);
        Assert.Equal("malformed", rejected["code"]);
        Assert.Equal("The element result could not be read.", rejected["message"]);
    }

    [Fact]
    public async Task Explore_selected_mep_composes_bounded_physical_topology()
    {
        var exploreTool = GitHubCopilotSessionBlueprint.CreateTools(
            new BimItAgentReadTools(new RecordingSession(_ => Error("unused", "unused")), InstanceId))
            .Single(tool => tool.Name == BimItAgentReadTools.ExploreSelectedMep);
        Assert.Equal(
            "Explore the physical MEP connectivity around the currently selected Revit elements. Includes HVAC, piping, electrical, conduit and cable tray physical connections when Revit exposes them. Takes no arguments.",
            exploreTool.Description);
        var schema = exploreTool.JsonSchema.GetRawText();
        Assert.DoesNotContain("instance_id", schema, StringComparison.Ordinal);
        Assert.DoesNotContain("document_id", schema, StringComparison.Ordinal);
        Assert.DoesNotContain("element_ref", schema, StringComparison.Ordinal);
        Assert.DoesNotContain("domain", schema, StringComparison.Ordinal);
        Assert.DoesNotContain("depth", schema, StringComparison.Ordinal);
        Assert.DoesNotContain("limit", schema, StringComparison.Ordinal);
        Assert.DoesNotContain("max_depth", schema, StringComparison.Ordinal);
        Assert.DoesNotContain("max_elements", schema, StringComparison.Ordinal);
        Assert.DoesNotContain("max_edges", schema, StringComparison.Ordinal);

        var populated = new RecordingSession(tool => tool switch
        {
            AssistantTools.GetSelection => Success("""
                {
                  "context": { "instance_id": "hidden-instance", "document_id": "doc-9" },
                  "selected_count": 8,
                  "truncated": true,
                  "element_refs": ["seed-z", "seed-a", "seed-m"]
                }
                """),
            AssistantTools.GetMepTopology => Success(TopologyJson(11, truncated: true, """["depth","edges"]""")),
            AssistantTools.GetElements => Success("""
                {
                  "context": { "instance_id": "hidden-instance", "document_id": "doc-9" },
                  "elements": [
                    { "element_ref": "seed-a", "status": "ok", "name": "PVC - DWV", "category_name": "Pipes", "family_name": "Pipe Types", "type_name": "PVC - DWV", "level_name": "L1" },
                    { "element_ref": "seed-z", "status": "ok", "name": "Supply", "category_name": "Ducts", "family_name": "Duct Types", "type_name": "Supply", "level_name": "L1" },
                    { "element_ref": "n1", "status": "ok", "name": "Elbow", "category_name": "Duct Fittings", "family_name": "Elbow", "type_name": "Standard", "level_name": "L1" },
                    { "element_ref": "n2", "status": "ok", "name": "Tee", "category_name": "Pipe Fittings", "family_name": "Tee", "type_name": "Standard", "level_name": "L1" },
                    { "element_ref": "n3", "status": "ok", "name": "Panel", "category_name": "Electrical Equipment", "family_name": "Panel", "type_name": "208V", "level_name": "L1" },
                    { "element_ref": "n4", "status": "ok", "name": "Tray", "category_name": "Cable Trays", "family_name": "Tray", "type_name": "Ladder", "level_name": "L1" },
                    { "element_ref": "n5", "status": "ok", "name": "Conduit", "category_name": "Conduits", "family_name": "Conduit", "type_name": "EMT", "level_name": "L1" },
                    { "element_ref": "n6", "status": "ok", "name": "Device", "category_name": "Electrical Fixtures", "family_name": "Receptacle", "type_name": "Duplex", "level_name": "L1" },
                    { "element_ref": "n7", "status": "ok", "name": "Fitting", "category_name": "Conduit Fittings", "family_name": "Coupling", "type_name": "Set Screw", "level_name": "L1" },
                    { "element_ref": "n8", "status": "not_found" }
                  ]
                }
                """),
            _ => Error("unexpected", "unexpected " + tool),
        });
        var tools = new BimItAgentReadTools(populated, InstanceId);
        var explored = await tools.InvokeAsync(BimItAgentReadTools.ExploreSelectedMep, CancellationToken.None);
        Assert.True((bool)explored["ok"]!);
        Assert.Equal(8, explored["selected_count"]);
        Assert.Equal(3, explored["seed_count"]);
        Assert.Equal(true, explored["selection_truncated"]);
        Assert.Equal(10, explored["described_count"]);
        Assert.Equal(true, explored["details_truncated"]);
        Assert.False(explored.ContainsKey("instance_id"));
        Assert.False(explored.ContainsKey("document_id"));
        var shaped = JsonSerializer.Serialize(explored);
        Assert.DoesNotContain("hidden-instance", shaped, StringComparison.Ordinal);
        Assert.DoesNotContain("doc-hidden", shaped, StringComparison.Ordinal);
        Assert.DoesNotContain("doc-9", shaped, StringComparison.Ordinal);
        Assert.DoesNotContain(populated.Calls, call => call.Tool == AssistantTools.GetContext);
        Assert.Equal(
            [AssistantTools.GetSelection, AssistantTools.GetMepTopology, AssistantTools.GetElements],
            populated.Calls.Select(call => call.Tool).ToArray());

        var selectionCall = populated.Calls[0];
        Assert.Equal(InstanceId, selectionCall.Arguments["instance_id"]);
        Assert.Equal(BimItAgentReadTools.ExploreSelectionLimit, selectionCall.Arguments["limit"]);
        Assert.Equal(3, selectionCall.Arguments["limit"]);
        Assert.Equal(2, selectionCall.Arguments.Count);

        var topologyCall = populated.Calls[1];
        Assert.Equal(InstanceId, topologyCall.Arguments["instance_id"]);
        Assert.Equal("doc-9", topologyCall.Arguments["document_id"]);
        Assert.Equal(["seed-z", "seed-a", "seed-m"], Assert.IsAssignableFrom<IReadOnlyList<string>>(topologyCall.Arguments["seed_element_refs"]));
        Assert.Equal(2, topologyCall.Arguments["max_depth"]);
        Assert.Equal(25, topologyCall.Arguments["max_elements"]);
        Assert.Equal(40, topologyCall.Arguments["max_edges"]);
        Assert.False(topologyCall.Arguments.ContainsKey("domain"));
        Assert.Equal(6, topologyCall.Arguments.Count);

        var elementsCall = populated.Calls[2];
        Assert.Equal(InstanceId, elementsCall.Arguments["instance_id"]);
        Assert.Equal("doc-9", elementsCall.Arguments["document_id"]);
        Assert.Equal(
            ["seed-a", "seed-z", "n1", "n2", "n3", "n4", "n5", "n6", "n7", "n8"],
            Assert.IsAssignableFrom<IReadOnlyList<string>>(elementsCall.Arguments["element_refs"]));
        var projection = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(elementsCall.Arguments["projection"]);
        Assert.Equal(
            ["name", "category_name", "family_name", "type_name", "level_name"],
            Assert.IsAssignableFrom<IReadOnlyList<string>>(projection["fields"]));
        Assert.Single(projection);

        var seeds = Assert.IsAssignableFrom<IReadOnlyList<IReadOnlyDictionary<string, object?>>>(explored["seeds"]);
        Assert.Equal(["ok", "not_found", "no_connectors"], seeds.Select(seed => seed["status"]).ToArray());
        Assert.Equal(["seed-z", "seed-a", "seed-m"], seeds.Select(seed => seed["element_ref"]).ToArray());
        var topology = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(explored["topology"]);
        Assert.Equal(11, topology["node_count"]);
        Assert.Equal(4, topology["edge_count"]);
        Assert.Equal(true, topology["truncated"]);
        Assert.Equal(["depth", "edges"], Assert.IsAssignableFrom<IReadOnlyList<string>>(topology["truncation_reasons"]));
        var nodes = Assert.IsAssignableFrom<IReadOnlyList<IReadOnlyDictionary<string, object?>>>(topology["nodes"]);
        Assert.Equal("seed-a", nodes[0]["element_ref"]);
        Assert.Equal(0, nodes[0]["depth"]);
        Assert.Equal(1, nodes[2]["depth"]);
        Assert.Equal("n9", nodes[10]["element_ref"]);
        var edges = Assert.IsAssignableFrom<IReadOnlyList<IReadOnlyDictionary<string, object?>>>(topology["edges"]);
        Assert.Equal(["hvac"], Assert.IsAssignableFrom<IReadOnlyList<string>>(edges[0]["domains"]));
        Assert.Equal(["piping"], Assert.IsAssignableFrom<IReadOnlyList<string>>(edges[1]["domains"]));
        Assert.Equal(["electrical"], Assert.IsAssignableFrom<IReadOnlyList<string>>(edges[2]["domains"]));
        Assert.Equal(["cable_tray_conduit"], Assert.IsAssignableFrom<IReadOnlyList<string>>(edges[3]["domains"]));
        var items = Assert.IsAssignableFrom<IReadOnlyList<IReadOnlyDictionary<string, object?>>>(explored["elements"]);
        Assert.Equal("not_found", items[9]["status"]);
        Assert.Equal("n8", items[9]["element_ref"]);
        Assert.Equal(
            ["element_ref", "status", "name", "category_name", "family_name", "type_name", "level_name"],
            items[0].Keys);
        Assert.False(items[0].ContainsKey("parameters"));
        Assert.Equal([BimItAgentReadTools.ExploreSelectedMep], tools.ConsumeUsedTools());

        var exactTen = new RecordingSession(tool => tool switch
        {
            AssistantTools.GetSelection => Success(SelectionJson(1, false, """["seed-a"]""")),
            AssistantTools.GetMepTopology => Success(TopologyJson(10, truncated: false, "[]")),
            _ => Success("""
                {
                  "context": { "instance_id": "hidden-instance", "document_id": "doc-9" },
                  "elements": [
                    { "element_ref": "seed-a", "status": "ok", "name": "A", "category_name": "Pipes", "family_name": "Pipe Types", "type_name": "A", "level_name": "L1" },
                    { "element_ref": "seed-z", "status": "ok", "name": "Z", "category_name": "Pipes", "family_name": "Pipe Types", "type_name": "Z", "level_name": "L1" },
                    { "element_ref": "n1", "status": "ok", "name": "1", "category_name": "Pipes", "family_name": "Pipe Types", "type_name": "1", "level_name": "L1" },
                    { "element_ref": "n2", "status": "ok", "name": "2", "category_name": "Pipes", "family_name": "Pipe Types", "type_name": "2", "level_name": "L1" },
                    { "element_ref": "n3", "status": "ok", "name": "3", "category_name": "Pipes", "family_name": "Pipe Types", "type_name": "3", "level_name": "L1" },
                    { "element_ref": "n4", "status": "ok", "name": "4", "category_name": "Pipes", "family_name": "Pipe Types", "type_name": "4", "level_name": "L1" },
                    { "element_ref": "n5", "status": "ok", "name": "5", "category_name": "Pipes", "family_name": "Pipe Types", "type_name": "5", "level_name": "L1" },
                    { "element_ref": "n6", "status": "ok", "name": "6", "category_name": "Pipes", "family_name": "Pipe Types", "type_name": "6", "level_name": "L1" },
                    { "element_ref": "n7", "status": "ok", "name": "7", "category_name": "Pipes", "family_name": "Pipe Types", "type_name": "7", "level_name": "L1" },
                    { "element_ref": "n8", "status": "ok", "name": "8", "category_name": "Pipes", "family_name": "Pipe Types", "type_name": "8", "level_name": "L1" }
                  ]
                }
                """),
        });
        var ten = await new BimItAgentReadTools(exactTen, InstanceId).InvokeAsync(BimItAgentReadTools.ExploreSelectedMep, CancellationToken.None);
        Assert.Equal(false, ten["details_truncated"]);
        Assert.Equal(10, ten["described_count"]);
        Assert.Equal(false, Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(ten["topology"])["truncated"]);
        Assert.Equal(10, Assert.IsAssignableFrom<IReadOnlyList<string>>(exactTen.Calls[2].Arguments["element_refs"]).Count);

        var emptySession = new RecordingSession(_ => Success(SelectionJson(0, false, "[]")));
        var empty = await new BimItAgentReadTools(emptySession, InstanceId).InvokeAsync(BimItAgentReadTools.ExploreSelectedMep, CancellationToken.None);
        Assert.True((bool)empty["ok"]!);
        Assert.Equal(0, empty["selected_count"]);
        Assert.Equal(0, empty["seed_count"]);
        Assert.Equal(false, empty["selection_truncated"]);
        Assert.Equal(0, empty["described_count"]);
        Assert.Equal(false, empty["details_truncated"]);
        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<IReadOnlyDictionary<string, object?>>>(empty["seeds"]));
        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<IReadOnlyDictionary<string, object?>>>(empty["elements"]));
        var emptyTopology = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(empty["topology"]);
        Assert.Equal(0, emptyTopology["node_count"]);
        Assert.Equal(0, emptyTopology["edge_count"]);
        Assert.Equal(false, emptyTopology["truncated"]);
        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<string>>(emptyTopology["truncation_reasons"]));
        Assert.Equal(AssistantTools.GetSelection, Assert.Single(emptySession.Calls).Tool);

        var bare = new RecordingSession(tool => tool == AssistantTools.GetSelection
            ? Success(SelectionJson(1, false, """["receptacle"]"""))
            : Success("""
                {
                  "context": { "instance_id": "hidden-instance", "document_id": "doc-9" },
                  "seeds": [ { "element_ref": "receptacle", "status": "no_connectors" } ],
                  "nodes": [],
                  "edges": [],
                  "truncated": false,
                  "truncation_reasons": []
                }
                """));
        var noConnectors = await new BimItAgentReadTools(bare, InstanceId).InvokeAsync(BimItAgentReadTools.ExploreSelectedMep, CancellationToken.None);
        Assert.True((bool)noConnectors["ok"]!);
        Assert.Equal("no_connectors", Assert.IsAssignableFrom<IReadOnlyList<IReadOnlyDictionary<string, object?>>>(noConnectors["seeds"])[0]["status"]);
        Assert.Equal(0, noConnectors["described_count"]);
        Assert.Equal(false, noConnectors["details_truncated"]);
        Assert.Equal([AssistantTools.GetSelection, AssistantTools.GetMepTopology], bare.Calls.Select(call => call.Tool).ToArray());

        var selectionError = await new BimItAgentReadTools(
            new RecordingSession(_ => Error("NO_ACTIVE_DOCUMENT", "No active Revit document is available. C:\\secret\\model.rvt ghp_secret")),
            InstanceId).InvokeAsync(BimItAgentReadTools.ExploreSelectedMep, CancellationToken.None);
        Assert.False((bool)selectionError["ok"]!);
        Assert.Equal("NO_ACTIVE_DOCUMENT", selectionError["code"]);
        var selectionMessage = Assert.IsType<string>(selectionError["message"]);
        Assert.DoesNotContain("C:\\secret", selectionMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("ghp_secret", selectionMessage, StringComparison.Ordinal);

        var topologyError = new RecordingSession(tool => tool == AssistantTools.GetSelection
            ? Success(SelectionJson(1, false, """["seed-a"]"""))
            : Error("DOCUMENT_CONTEXT_CHANGED", "The supplied document_id does not match the active document. C:\\secret\\model.rvt"));
        var staleTopology = await new BimItAgentReadTools(topologyError, InstanceId).InvokeAsync(BimItAgentReadTools.ExploreSelectedMep, CancellationToken.None);
        Assert.False((bool)staleTopology["ok"]!);
        Assert.Equal("DOCUMENT_CONTEXT_CHANGED", staleTopology["code"]);
        Assert.DoesNotContain("C:\\secret", Assert.IsType<string>(staleTopology["message"]), StringComparison.Ordinal);
        Assert.Equal(2, topologyError.Calls.Count);

        var elementsError = new RecordingSession(tool => tool switch
        {
            AssistantTools.GetSelection => Success(SelectionJson(1, false, """["seed-a"]""")),
            AssistantTools.GetMepTopology => Success(TopologyJson(1, truncated: false, "[]")),
            _ => Error("DOCUMENT_CONTEXT_CHANGED", "The supplied document_id does not match the active document."),
        });
        var staleElements = await new BimItAgentReadTools(elementsError, InstanceId).InvokeAsync(BimItAgentReadTools.ExploreSelectedMep, CancellationToken.None);
        Assert.Equal("DOCUMENT_CONTEXT_CHANGED", staleElements["code"]);
        Assert.Equal(3, elementsError.Calls.Count);

        var elementsFailure = new RecordingSession(tool => tool switch
        {
            AssistantTools.GetSelection => Success(SelectionJson(1, false, """["seed-a"]""")),
            AssistantTools.GetMepTopology => Success(TopologyJson(1, truncated: false, "[]")),
            _ => Error("REVIT_EXECUTION_FAILED", "The Revit call failed."),
        });
        var failedElements = await new BimItAgentReadTools(elementsFailure, InstanceId).InvokeAsync(BimItAgentReadTools.ExploreSelectedMep, CancellationToken.None);
        Assert.Equal("REVIT_EXECUTION_FAILED", failedElements["code"]);
        Assert.Equal(3, elementsFailure.Calls.Count);

        await AssertMalformedTopologyAsync("""{ "context": { "instance_id": "hidden-instance", "document_id": "doc-9" }, "seeds": [ { "element_ref": "seed-a", "status": "circuit" } ], "nodes": [], "edges": [], "truncated": false, "truncation_reasons": [] }""");
        await AssertMalformedTopologyAsync("""{ "context": { "instance_id": "hidden-instance", "document_id": "doc-9" }, "seeds": [ { "element_ref": "seed-a", "status": "ok" } ], "nodes": [ { "element_ref": "seed-a", "depth": 0 } ], "edges": [ { "element_ref_a": "seed-a", "element_ref_b": "n1", "domains": ["logical"] } ], "truncated": false, "truncation_reasons": [] }""");
        await AssertMalformedTopologyAsync("""{ "context": { "instance_id": "hidden-instance", "document_id": "doc-9" }, "seeds": [ { "element_ref": "seed-a", "status": "ok" } ], "nodes": [ { "element_ref": "seed-a", "depth": 0 } ], "edges": [], "truncated": true, "truncation_reasons": ["circuits"] }""");
        await AssertMalformedTopologyAsync("""{ "context": { "instance_id": "hidden-instance", "document_id": "doc-9" }, "seeds": [ { "element_ref": "seed-a", "status": 99 } ], "nodes": [], "edges": [], "truncated": false, "truncation_reasons": [] }""");
    }

    [Fact]
    public async Task Warnings_do_not_invent_a_document_id_and_keep_mcp_errors_structured()
    {
        var missing = new BimItAgentReadTools(new RecordingSession(_ => Success(ContextJson(null))), InstanceId);
        var blocked = await missing.InvokeAsync(BimItAgentReadTools.Warnings, CancellationToken.None);
        Assert.Equal("bridge_v12_required", blocked["code"]);
        Assert.False((bool)blocked["ok"]!);

        var none = new BimItAgentReadTools(new RecordingSession(_ => Success("""{"document":null,"active_view":null}""")), InstanceId);
        var noDocument = await none.InvokeAsync(BimItAgentReadTools.Warnings, CancellationToken.None);
        Assert.Equal(CapabilityErrorCodes.NoActiveDocument, noDocument["code"]);

        var failed = new BimItAgentReadTools(
            new RecordingSession(tool => tool == AssistantTools.GetContext
                ? Success(ContextJson("doc-9"))
                : Error("NO_REVIT_INSTANCE", "No eligible Revit instance is available. Token ghp_secret and C:\\secret\\model.rvt")),
            InstanceId);
        var error = await failed.InvokeAsync(BimItAgentReadTools.Warnings, CancellationToken.None);
        Assert.Equal("NO_REVIT_INSTANCE", error["code"]);
        var message = Assert.IsType<string>(error["message"]);
        Assert.DoesNotContain("ghp_secret", message, StringComparison.Ordinal);
        Assert.DoesNotContain("C:\\secret", message, StringComparison.Ordinal);
        Assert.Contains("[redacted]", message, StringComparison.Ordinal);
        Assert.Contains("[path]", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Natural_language_uses_the_runtime_and_slash_workflows_do_not()
    {
        var session = new RecordingSession(_ => Success(ContextJson("doc-9")));
        var runtime = new FakeRuntime();
        var pipe = AssistantPipeNames.Create(Environment.ProcessId, Guid.NewGuid().ToString("N"));
        var host = AssistantHost.RunAsync(
            new AssistantHostOptions(pipe, InstanceId, null, "2026", "26.5.0.55"),
            session,
            runtime);

        await using var client = AssistantPipeStreams.CreateClient(pipe);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await client.ConnectAsync(5_000, timeout.Token);
        await AssistantPipeFrames.WriteAsync(client, AssistantIpc.Serialize(Hello()), timeout.Token);
        var status = AssistantIpc.Parse(await AssistantPipeFrames.ReadAsync(client, timeout.Token));
        Assert.Equal("ready", status.Message!.McpState);
        Assert.True(status.Message.ProviderState is "starting" or "ready");
        await WaitFor(() => runtime.Initializations == 1);

        await AssistantPipeFrames.WriteAsync(
            client,
            AssistantIpc.Serialize(new AssistantIpcMessage(
                AssistantIpcKind.Chat, null, null, null, false, "chat-1", null, null, null, null, null, null, null, null, false, Text: "Analyse les warnings.")),
            timeout.Token);
        var chat = AssistantIpc.Parse(await AssistantPipeFrames.ReadAsync(client, timeout.Token));
        Assert.Equal(AssistantIpcKind.ChatResult, chat.Message!.Kind);
        Assert.Equal("There are 7 warnings.", chat.Message.Body);
        Assert.Equal(["Analyse les warnings."], runtime.Prompts);
        Assert.Empty(session.Calls);

        await AssistantPipeFrames.WriteAsync(
            client,
            AssistantIpc.Serialize(new AssistantIpcMessage(
                AssistantIpcKind.RunWorkflow, null, null, null, false, "req-1", AssistantWorkflowName.Context, null, null, null, null, null, null, null, false)),
            timeout.Token);
        var workflow = AssistantIpc.Parse(await AssistantPipeFrames.ReadAsync(client, timeout.Token));
        Assert.Equal(AssistantIpcKind.WorkflowResult, workflow.Message!.Kind);
        Assert.Empty(runtime.Prompts.Skip(1));
        Assert.Equal(AssistantTools.GetContext, Assert.Single(session.Calls).Tool);

        await AssistantPipeFrames.WriteAsync(
            client,
            AssistantIpc.Serialize(new AssistantIpcMessage(
                AssistantIpcKind.ResetConversation, null, null, null, false, null, null, null, null, null, null, null, null, null, false)),
            timeout.Token);
        var reset = AssistantIpc.Parse(await AssistantPipeFrames.ReadAsync(client, timeout.Token));
        Assert.Equal(AssistantIpcKind.Status, reset.Message!.Kind);
        Assert.Equal("ready", reset.Message.McpState);
        Assert.Equal(1, runtime.Resets);

        await AssistantPipeFrames.WriteAsync(
            client,
            AssistantIpc.Serialize(new AssistantIpcMessage(
                AssistantIpcKind.Shutdown, null, null, null, false, null, null, null, null, null, null, null, null, null, false)),
            timeout.Token);
        Assert.Equal(0, await host);
        Assert.True(runtime.Disposed);
    }

    [Fact]
    public async Task Provider_startup_failure_does_not_stop_the_assistant()
    {
        var session = new RecordingSession(_ => Success(ContextJson("doc-9")));
        var runtime = new FakeRuntime { FailInitialization = true };
        var pipe = AssistantPipeNames.Create(Environment.ProcessId, Guid.NewGuid().ToString("N"));
        var host = AssistantHost.RunAsync(new AssistantHostOptions(pipe, InstanceId, null, null, null), session, runtime);
        await using var client = AssistantPipeStreams.CreateClient(pipe);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await client.ConnectAsync(5_000, timeout.Token);
        await AssistantPipeFrames.WriteAsync(client, AssistantIpc.Serialize(Hello()), timeout.Token);
        var status = AssistantIpc.Parse(await AssistantPipeFrames.ReadAsync(client, timeout.Token));
        Assert.Equal("ready", status.Message!.McpState);
        await WaitFor(() => runtime.Initializations == 1);
        Assert.Equal("unavailable", runtime.Status.StateToken);

        await AssistantPipeFrames.WriteAsync(
            client,
            AssistantIpc.Serialize(new AssistantIpcMessage(
                AssistantIpcKind.RunWorkflow, null, null, null, false, "req-1", AssistantWorkflowName.Context, null, null, null, null, null, null, null, false)),
            timeout.Token);
        var workflow = AssistantIpc.Parse(await AssistantPipeFrames.ReadAsync(client, timeout.Token));
        Assert.False(workflow.Message!.IsError);
        await AssistantPipeFrames.WriteAsync(
            client,
            AssistantIpc.Serialize(new AssistantIpcMessage(
                AssistantIpcKind.Shutdown, null, null, null, false, null, null, null, null, null, null, null, null, null, false)),
            timeout.Token);
        Assert.Equal(0, await host);
    }

    [Fact]
    public async Task Provider_initialization_timeout_leaves_mcp_ready()
    {
        var session = new RecordingSession(_ => Success(ContextJson("doc-9")));
        var runtime = new FakeRuntime { HangInitialization = true };
        var chat = await ChatThenWorkflowAsync(session, runtime, FastProviderTimeouts());
        Assert.Equal(AssistantIpcKind.ChatResult, chat.Chat.Kind);
        Assert.True(chat.Chat.IsError);
        Assert.Equal("unavailable", chat.Chat.ProviderState);
        Assert.Null(chat.Chat.McpState);
        Assert.Equal("ready", chat.FollowUp!.McpState);
        Assert.Equal("ready", chat.FollowUp.AssistantState);
        Assert.Equal("unavailable", chat.FollowUp.ProviderState);
        Assert.False(chat.Workflow!.IsError);
        Assert.Equal(AssistantTools.GetContext, Assert.Single(session.Calls).Tool);
    }

    [Fact]
    public async Task Provider_reset_timeout_leaves_mcp_ready()
    {
        var session = new RecordingSession(_ => Success(ContextJson("doc-9")));
        var runtime = new FakeRuntime { HangReset = true };
        var pipe = AssistantPipeNames.Create(Environment.ProcessId, Guid.NewGuid().ToString("N"));
        var host = AssistantHost.RunAsync(
            new AssistantHostOptions(pipe, InstanceId, null, "2026", "26.5.0.55"),
            session,
            runtime,
            providerTimeouts: FastProviderTimeouts());
        await using var client = AssistantPipeStreams.CreateClient(pipe);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await client.ConnectAsync(5_000, timeout.Token);
        await AssistantPipeFrames.WriteAsync(client, AssistantIpc.Serialize(Hello()), timeout.Token);
        var started = AssistantIpc.Parse(await AssistantPipeFrames.ReadAsync(client, timeout.Token));
        Assert.Equal("ready", started.Message!.McpState);

        await AssistantPipeFrames.WriteAsync(
            client,
            AssistantIpc.Serialize(new AssistantIpcMessage(
                AssistantIpcKind.ResetConversation, null, null, null, false, null, null, null, null, null, null, null, null, null, false)),
            timeout.Token);
        var reset = AssistantIpc.Parse(await AssistantPipeFrames.ReadAsync(client, timeout.Token));
        Assert.Equal(AssistantIpcKind.Status, reset.Message!.Kind);
        Assert.Equal("ready", reset.Message.AssistantState);
        Assert.Equal("ready", reset.Message.McpState);
        Assert.Equal("unavailable", reset.Message.ProviderState);

        await AssistantPipeFrames.WriteAsync(
            client,
            AssistantIpc.Serialize(new AssistantIpcMessage(
                AssistantIpcKind.RunWorkflow, null, null, null, false, "req-1", AssistantWorkflowName.Context, null, null, null, null, null, null, null, false)),
            timeout.Token);
        var workflow = AssistantIpc.Parse(await AssistantPipeFrames.ReadAsync(client, timeout.Token));
        Assert.False(workflow.Message!.IsError);
        await ShutdownAsync(client, host, timeout.Token);
    }

    [Fact]
    public async Task Used_tool_trace_is_names_only_and_secrets_do_not_cross_ipc()
    {
        var session = new RecordingSession(_ => Success(ContextJson("doc-9")));
        var runtime = new FakeRuntime();
        runtime.ToolScripts.Enqueue(["bimit_get_warnings"]);
        runtime.ToolScripts.Enqueue([]);
        runtime.ToolScripts.Enqueue(["bimit_get_warnings", "revit_apply_parameter_updates", "instance_id", "document_id"]);
        var pipe = AssistantPipeNames.Create(Environment.ProcessId, Guid.NewGuid().ToString("N"));
        var host = AssistantHost.RunAsync(
            new AssistantHostOptions(pipe, InstanceId, null, "2026", "26.5.0.55"),
            session,
            runtime);
        await using var client = AssistantPipeStreams.CreateClient(pipe);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await client.ConnectAsync(5_000, timeout.Token);
        await AssistantPipeFrames.WriteAsync(client, AssistantIpc.Serialize(Hello()), timeout.Token);
        Assert.Equal("ready", AssistantIpc.Parse(await AssistantPipeFrames.ReadAsync(client, timeout.Token)).Message!.McpState);

        var warnings = await ChatRawAsync(client, "Analyse les warnings.", timeout.Token);
        Assert.Equal(["bimit_get_warnings"], warnings.Message!.UsedTools);
        Assert.DoesNotContain("instance_id", warnings.Raw, StringComparison.Ordinal);
        Assert.DoesNotContain("document_id", warnings.Raw, StringComparison.Ordinal);

        var none = await ChatRawAsync(client, "Combien y en a?", timeout.Token);
        Assert.Empty(none.Message!.UsedTools!);

        var poisoned = await ChatRawAsync(client, "again", timeout.Token);
        Assert.Equal(["bimit_get_warnings"], poisoned.Message!.UsedTools);
        Assert.DoesNotContain("revit_apply_parameter_updates", poisoned.Raw, StringComparison.Ordinal);
        Assert.DoesNotContain("instance_id", poisoned.Raw, StringComparison.Ordinal);
        Assert.DoesNotContain("document_id", poisoned.Raw, StringComparison.Ordinal);

        runtime.SecretError = "token ghu_secret ghs_secret ghr_secret";
        var secret = await ChatRawAsync(client, "secret", timeout.Token);
        Assert.DoesNotContain("ghu_", secret.Raw, StringComparison.Ordinal);
        Assert.DoesNotContain("ghs_", secret.Raw, StringComparison.Ordinal);
        Assert.DoesNotContain("ghr_", secret.Raw, StringComparison.Ordinal);
        Assert.Contains("[redacted]", secret.Raw, StringComparison.Ordinal);
        await ShutdownAsync(client, host, timeout.Token);

        Assert.Equal(TimeSpan.FromSeconds(30), AssistantProviderTimeouts.Default.Initialization);
        Assert.Equal(TimeSpan.FromSeconds(15), AssistantProviderTimeouts.Default.Reset);
        Assert.Equal(TimeSpan.FromSeconds(90), AssistantProviderTimeouts.Default.Turn);
        var addinTimeouts = File.ReadAllText(Path.Combine(RepoRoot(), "src", "BimIt.Revit.Addin", "Assistant", "AssistantIpcTimeouts.cs"));
        Assert.Contains("TimeSpan.FromSeconds(120)", addinTimeouts, StringComparison.Ordinal);
        Assert.True(AssistantProviderTimeouts.Default.Turn < TimeSpan.FromSeconds(120));
        Assert.True(AssistantProviderTimeouts.Default.Reset < TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void Ipc_chat_messages_are_bounded_and_reject_credentials()
    {
        var chat = AssistantIpc.Parse("""{"type":"chat","requestId":"abc","text":"Analyse les warnings."}""");
        Assert.Equal("Analyse les warnings.", chat.Message!.Text);
        Assert.False(AssistantIpc.Parse("""{"type":"chat","requestId":"abc","text":"hi","token":"ghp_secret"}""").Accepted);
        Assert.False(AssistantIpc.Parse("""{"type":"chat","requestId":"abc","text":"hi","githubToken":"secret"}""").Accepted);
        Assert.False(AssistantIpc.Parse("""{"type":"chat","requestId":"abc","text":"hi","apiKey":"secret"}""").Accepted);
        Assert.False(AssistantIpc.Parse("""{"type":"status","assistantState":"ready","mcpState":"ready","authorization":"bearer secret"}""").Accepted);
        Assert.False(AssistantIpc.Parse("""{"type":"chat","requestId":"abc","text":""}""").Accepted);
        Assert.False(AssistantIpc.Parse("{\"type\":\"chat\",\"requestId\":\"abc\",\"text\":\"" + new string('a', 2_001) + "\"}").Accepted);
        Assert.False(AssistantIpc.Parse("""{"type":"queryStatus","text":"hi"}""").Accepted);
        Assert.False(AssistantIpc.Parse("""{"type":"resetConversation","instanceId":"x"}""").Accepted);
        Assert.False(AssistantIpc.GrantsApprovalAuthority);
        Assert.False(AssistantIpc.Parse("""{"type":"chatResult","requestId":"abc","title":"BIM-IT Assistant","body":"x","usedTools":["revit_apply_parameter_updates"]}""").Accepted);
        Assert.False(AssistantIpc.Parse("""{"type":"chatResult","requestId":"abc","title":"BIM-IT Assistant","body":"x","usedTools":["instance_id"]}""").Accepted);
        Assert.False(AssistantIpc.Parse("""{"type":"chatResult","requestId":"abc","title":"BIM-IT Assistant","body":"x","usedTools":["bimit_get_warnings","document_id"]}""").Accepted);
        Assert.False(AssistantIpc.Parse("""{"type":"chat","requestId":"abc","text":"hi","usedTools":["bimit_get_warnings"]}""").Accepted);
        var warningsTrace = AssistantIpc.Parse("""{"type":"chatResult","requestId":"abc","title":"BIM-IT Assistant","body":"x","usedTools":["bimit_get_warnings"]}""");
        Assert.Equal(["bimit_get_warnings"], warningsTrace.Message!.UsedTools);
        var selectionTrace = AssistantIpc.Parse("""{"type":"chatResult","requestId":"abc","title":"BIM-IT Assistant","body":"x","usedTools":["bimit_inspect_selection"]}""");
        Assert.Equal(["bimit_inspect_selection"], selectionTrace.Message!.UsedTools);
        var mepTrace = AssistantIpc.Parse("""{"type":"chatResult","requestId":"abc","title":"BIM-IT Assistant","body":"x","usedTools":["bimit_explore_selected_mep"]}""");
        Assert.Equal(["bimit_explore_selected_mep"], mepTrace.Message!.UsedTools);
        Assert.False(AssistantIpc.Parse("""{"type":"chatResult","requestId":"abc","title":"BIM-IT Assistant","body":"x","usedTools":["revit_get_selection"]}""").Accepted);
        Assert.False(AssistantIpc.Parse("""{"type":"chatResult","requestId":"abc","title":"BIM-IT Assistant","body":"x","usedTools":["revit_get_elements"]}""").Accepted);
        Assert.False(AssistantIpc.Parse("""{"type":"chatResult","requestId":"abc","title":"BIM-IT Assistant","body":"x","usedTools":["revit_get_mep_topology"]}""").Accepted);
        var emptyTrace = AssistantIpc.Parse("""{"type":"chatResult","requestId":"abc","title":"BIM-IT Assistant","body":"x","usedTools":[]}""");
        Assert.Empty(emptyTrace.Message!.UsedTools!);

        var serialized = AssistantIpc.Serialize(new AssistantIpcMessage(
            AssistantIpcKind.ChatResult,
            null,
            null,
            null,
            false,
            "abc",
            null,
            null,
            null,
            null,
            null,
            null,
            "BIM-IT Assistant",
            "Seven warnings.",
            false,
            "GitHub Copilot",
            "ready",
            "auto",
            null,
            "GitHub Copilot · auto"));
        Assert.DoesNotContain("token", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(AssistantIpcKind.ChatResult, AssistantIpc.Parse(serialized).Message!.Kind);
    }

    [Fact]
    public void Provider_runtime_directory_is_per_process_and_removed()
    {
        var first = CopilotRuntimeDirectory.Create();
        var second = CopilotRuntimeDirectory.Create();
        Assert.NotEqual(first, second);
        Assert.Contains(Environment.ProcessId + "-", Path.GetFileName(first), StringComparison.Ordinal);
        Assert.Contains(Path.Combine("BIM-IT", "Assistant", "CopilotRuntime"), first, StringComparison.Ordinal);
        CopilotRuntimeDirectory.TryDelete(first);
        CopilotRuntimeDirectory.TryDelete(second);
        Assert.False(Directory.Exists(first));
        Assert.False(Directory.Exists(second));
    }

    [Fact]
    public void Runtime_directory_delete_retries_a_busy_directory_then_stops()
    {
        var waits = new List<TimeSpan>();
        var deletes = 0;
        CopilotRuntimeDirectory.TryDelete(
            "runtime-dir",
            attempts: 3,
            delay: TimeSpan.FromMilliseconds(10),
            wait: waits.Add,
            exists: _ => true,
            delete: (_, _) =>
            {
                deletes++;
                if (deletes < 3)
                {
                    throw new IOException("busy");
                }
            });

        Assert.Equal(3, deletes);
        Assert.Equal(2, waits.Count);
        Assert.Equal(TimeSpan.FromMilliseconds(10), waits[0]);
        Assert.Equal(TimeSpan.FromMilliseconds(20), waits[1]);

        var exhausted = 0;
        var exhaustedWaits = 0;
        CopilotRuntimeDirectory.TryDelete(
            "runtime-dir",
            attempts: CopilotRuntimeDirectory.DeleteAttempts,
            delay: CopilotRuntimeDirectory.DeleteDelay,
            wait: _ => exhaustedWaits++,
            exists: _ => true,
            delete: (_, _) =>
            {
                exhausted++;
                throw new UnauthorizedAccessException("locked");
            });
        Assert.Equal(CopilotRuntimeDirectory.DeleteAttempts, exhausted);
        Assert.Equal(CopilotRuntimeDirectory.DeleteAttempts - 1, exhaustedWaits);
    }

    [Fact]
    public void Used_tool_buffer_is_cleared_after_the_turn_gate_is_acquired()
    {
        var source = File.ReadAllText(Path.Combine(
            RepoRoot(),
            "src",
            "BimIt.Revit.Assistant",
            "Providers",
            "GitHubCopilot",
            "GitHubCopilotAgentRuntime.cs"));
        var sendStart = source.IndexOf("public async Task<AssistantAgentTurn> SendAsync", StringComparison.Ordinal);
        var sendEnd = source.IndexOf("public async Task ResetConversationAsync", StringComparison.Ordinal);
        var send = source[sendStart..sendEnd];
        var wait = send.IndexOf("await _gate.WaitAsync", StringComparison.Ordinal);
        var clear = send.IndexOf("_tools.ConsumeUsedTools()", StringComparison.Ordinal);
        Assert.True(wait >= 0 && clear > wait);
    }

    [Fact]
    public void Assistant_source_does_not_create_a_chat_transcript_store()
    {
        var root = Path.Combine(RepoRoot(), "src", "BimIt.Revit.Assistant");
        var source = string.Join('\n', Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories).Select(File.ReadAllText));
        Assert.DoesNotContain("Sqlite", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("EnableSessionStore = true", source, StringComparison.Ordinal);
        Assert.DoesNotContain("conversation.json", source, StringComparison.Ordinal);
        Assert.Contains("EnableSessionStore = false", source, StringComparison.Ordinal);
        Assert.Contains("ChildProcessLifetime.BindToThisProcess()", File.ReadAllText(Path.Combine(root, "AssistantHost.cs")), StringComparison.Ordinal);
    }

    private static AssistantProviderTimeouts FastProviderTimeouts()
    {
        return new AssistantProviderTimeouts(TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(400));
    }

    private static async Task<ChatFollowUp> ChatThenWorkflowAsync(
        RecordingSession session,
        FakeRuntime runtime,
        AssistantProviderTimeouts timeouts)
    {
        var pipe = AssistantPipeNames.Create(Environment.ProcessId, Guid.NewGuid().ToString("N"));
        var host = AssistantHost.RunAsync(
            new AssistantHostOptions(pipe, InstanceId, null, "2026", "26.5.0.55"),
            session,
            runtime,
            providerTimeouts: timeouts);
        await using var client = AssistantPipeStreams.CreateClient(pipe);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await client.ConnectAsync(5_000, timeout.Token);
        await AssistantPipeFrames.WriteAsync(client, AssistantIpc.Serialize(Hello()), timeout.Token);
        _ = await AssistantPipeFrames.ReadAsync(client, timeout.Token);
        var chat = await ChatRawAsync(client, "Analyse les warnings.", timeout.Token);
        await AssistantPipeFrames.WriteAsync(
            client,
            AssistantIpc.Serialize(new AssistantIpcMessage(
                AssistantIpcKind.QueryStatus, null, null, null, false, null, null, null, null, null, null, null, null, null, false)),
            timeout.Token);
        var followUp = AssistantIpc.Parse(await AssistantPipeFrames.ReadAsync(client, timeout.Token));
        await AssistantPipeFrames.WriteAsync(
            client,
            AssistantIpc.Serialize(new AssistantIpcMessage(
                AssistantIpcKind.RunWorkflow, null, null, null, false, "req-1", AssistantWorkflowName.Context, null, null, null, null, null, null, null, false)),
            timeout.Token);
        var workflow = AssistantIpc.Parse(await AssistantPipeFrames.ReadAsync(client, timeout.Token));
        await ShutdownAsync(client, host, timeout.Token);
        return new ChatFollowUp(chat.Message!, followUp.Message, workflow.Message);
    }

    private static async Task<RawChat> ChatRawAsync(NamedPipeClientStream client, string text, CancellationToken cancellationToken)
    {
        await AssistantPipeFrames.WriteAsync(
            client,
            AssistantIpc.Serialize(new AssistantIpcMessage(
                AssistantIpcKind.Chat, null, null, null, false, "chat-1", null, null, null, null, null, null, null, null, false, Text: text)),
            cancellationToken);
        var raw = await AssistantPipeFrames.ReadAsync(client, cancellationToken);
        return new RawChat(raw ?? "", AssistantIpc.Parse(raw).Message);
    }

    private static async Task ShutdownAsync(NamedPipeClientStream client, Task<int> host, CancellationToken cancellationToken)
    {
        await AssistantPipeFrames.WriteAsync(
            client,
            AssistantIpc.Serialize(new AssistantIpcMessage(
                AssistantIpcKind.Shutdown, null, null, null, false, null, null, null, null, null, null, null, null, null, false)),
            cancellationToken);
        Assert.Equal(0, await host);
    }

    private sealed record ChatFollowUp(AssistantIpcMessage Chat, AssistantIpcMessage? FollowUp, AssistantIpcMessage? Workflow);

    private sealed record RawChat(string Raw, AssistantIpcMessage? Message);

    private static AssistantIpcMessage Hello()
    {
        return new AssistantIpcMessage(AssistantIpcKind.Hello, InstanceId, "2026", "26.5.0.55", true, null, null, null, null, null, null, null, null, null, false);
    }

    private static string ContextJson(string? documentId)
    {
        var result = new GetContextResult
        {
            Instance = new GetContextInstance { InstanceId = InstanceId, RevitVersion = "2026", RevitBuild = "26.5.0.55" },
            Document = new GetContextDocument
            {
                Title = "Sample",
                Kind = GetContextDocumentKind.Project,
                IsWorkshared = false,
                IsModelInCloud = false,
                IsReadOnly = false,
                IsModified = false,
            },
            ActiveView = new GetContextActiveView { ElementId = "view-1", Name = "Level 1", ViewType = "FloorPlan" },
            Selection = new GetContextSelection { Count = 0 },
        };
        var node = JsonNode.Parse(JsonSerializer.Serialize(result, ContractJson.Options))!;
        if (documentId is not null)
        {
            node["document"]!["document_id"] = documentId;
        }

        return node.ToJsonString();
    }

    private static async Task AssertMalformedTopologyAsync(string topologyJson)
    {
        var session = new RecordingSession(tool => tool == AssistantTools.GetSelection
            ? Success(SelectionJson(1, false, """["seed-a"]"""))
            : Success(topologyJson));
        var rejected = await new BimItAgentReadTools(session, InstanceId).InvokeAsync(BimItAgentReadTools.ExploreSelectedMep, CancellationToken.None);
        Assert.False((bool)rejected["ok"]!);
        Assert.Equal("malformed", rejected["code"]);
        Assert.Equal("The topology result could not be read.", rejected["message"]);
        Assert.Equal([AssistantTools.GetSelection, AssistantTools.GetMepTopology], session.Calls.Select(call => call.Tool).ToArray());
    }

    private static string SelectionJson(int selectedCount, bool truncated, string refs)
    {
        return $$"""
            {
              "context": { "instance_id": "hidden-instance", "document_id": "doc-9" },
              "selected_count": {{selectedCount}},
              "truncated": {{(truncated ? "true" : "false")}},
              "element_refs": {{refs}}
            }
            """;
    }

    private static string TopologyJson(int nodeCount, bool truncated, string reasons)
    {
        var nodeRefs = new[] { "seed-a", "seed-z", "n1", "n2", "n3", "n4", "n5", "n6", "n7", "n8", "n9" };
        var nodes = string.Join(",", nodeRefs.Take(nodeCount).Select((elementRef, index) =>
        {
            var depth = index < 2 ? 0 : 1;
            return $$$"""{"element_ref":"{{{elementRef}}}","depth":{{{depth}}}}""";
        }));
        var edges = nodeCount < 2
            ? ""
            : """
              { "element_ref_a": "seed-a", "element_ref_b": "seed-z", "domains": ["hvac"] },
              { "element_ref_a": "seed-a", "element_ref_b": "n1", "domains": ["piping"] },
              { "element_ref_a": "n1", "element_ref_b": "n3", "domains": ["electrical"] },
              { "element_ref_a": "n4", "element_ref_b": "n5", "domains": ["cable_tray_conduit"] }
              """;
        return $$"""
            {
              "context": { "instance_id": "hidden-instance", "document_id": "doc-hidden" },
              "seeds": [
                { "element_ref": "seed-z", "status": "ok" },
                { "element_ref": "seed-a", "status": "not_found" },
                { "element_ref": "seed-m", "status": "no_connectors" }
              ],
              "nodes": [{{nodes}}],
              "edges": [{{edges}}],
              "truncated": {{(truncated ? "true" : "false")}},
              "truncation_reasons": {{reasons}}
            }
            """;
    }

    private static AssistantToolCall Success(string json)
    {
        using var document = JsonDocument.Parse(json);
        return new AssistantToolCall(false, null, null, document.RootElement.Clone());
    }

    private static AssistantToolCall Error(string code, string message)
    {
        return new AssistantToolCall(true, code, message, null);
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(15);
        }

        Assert.True(condition());
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

        throw new InvalidOperationException("Repository root was not found.");
    }

    private sealed class RecordingSession(Func<string, AssistantToolCall> responder) : IAssistantToolSession
    {
        public List<Call> Calls { get; } = [];

        public Task<AssistantMcpReadiness> InitializeAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new AssistantMcpReadiness(true, 3, AssistantTools.WorkflowTools, null));
        }

        public Task<AssistantToolCall> CallAsync(string toolName, IReadOnlyDictionary<string, object?> arguments, CancellationToken cancellationToken)
        {
            Calls.Add(new Call(toolName, new Dictionary<string, object?>(arguments)));
            return Task.FromResult(responder(toolName));
        }

        public sealed record Call(string Tool, Dictionary<string, object?> Arguments);
    }

    private sealed class FakeRuntime : IAssistantAgentRuntime
    {
        public bool FailInitialization { get; init; }

        public bool HangInitialization { get; init; }

        public bool HangReset { get; init; }

        public string? SecretError { get; set; }

        public Queue<IReadOnlyList<string>?> ToolScripts { get; } = new();

        public int Initializations { get; private set; }

        public int Resets { get; private set; }

        public bool Disposed { get; private set; }

        public List<string> Prompts { get; } = [];

        public AssistantProviderStatus Status { get; private set; } = new("GitHub Copilot", AssistantProviderState.Starting, null, null);

        public async Task InitializeAsync(CancellationToken cancellationToken)
        {
            Initializations++;
            if (HangInitialization)
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    Status = new AssistantProviderStatus("GitHub Copilot", AssistantProviderState.Unavailable, null, AssistantProviderTimeouts.InitializationTimedOut);
                    return;
                }
            }

            Status = FailInitialization
                ? new AssistantProviderStatus("GitHub Copilot", AssistantProviderState.Unavailable, null, "GitHub Copilot sign-in is required.")
                : new AssistantProviderStatus("GitHub Copilot", AssistantProviderState.Ready, "auto", null);
        }

        public Task<AssistantAgentTurn> SendAsync(string userText, CancellationToken cancellationToken)
        {
            Prompts.Add(userText);
            var tools = ToolScripts.Count > 0 ? ToolScripts.Dequeue() : null;
            if (SecretError is not null)
            {
                Status = new AssistantProviderStatus("GitHub Copilot", AssistantProviderState.Unavailable, null, SecretError);
                return Task.FromResult(new AssistantAgentTurn("BIM-IT Assistant", SecretError, true, tools));
            }

            return Task.FromResult(new AssistantAgentTurn("BIM-IT Assistant", "There are 7 warnings.", false, tools));
        }

        public async Task ResetConversationAsync(CancellationToken cancellationToken)
        {
            if (HangReset)
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    Status = new AssistantProviderStatus("GitHub Copilot", AssistantProviderState.Unavailable, null, AssistantProviderTimeouts.ResetTimedOut);
                    Resets++;
                    return;
                }
            }

            Resets++;
            Prompts.Clear();
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
