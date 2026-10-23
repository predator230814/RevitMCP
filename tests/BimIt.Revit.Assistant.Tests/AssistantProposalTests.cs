using System.IO.Pipes;
using System.Text.Json;
using BimIt.Revit.Assistant.Ipc;
using BimIt.Revit.Assistant.Patterns;
using BimIt.Revit.Assistant.Providers.GitHubCopilot;
using BimIt.Revit.Contracts;
using Xunit;

namespace BimIt.Revit.Assistant.Tests;

public sealed class AssistantProposalTests
{
    private const string InstanceId = "11111111-2222-3333-4444-555555555555";

    private const string HiddenDocument = "doc-hidden";

    private const string HiddenTemplate = "tmpl-hidden";

    [Fact]
    public void Model_surface_exposes_seven_tools_and_hides_raw_writes()
    {
        Assert.Equal(
            [
                BimItAgentReadTools.ModelContext,
                BimItAgentReadTools.Warnings,
                BimItAgentReadTools.ActiveViewElements,
                BimItAgentReadTools.InspectSelection,
                BimItAgentReadTools.ExploreSelectedMep,
                MepDevicePlacementProposalTool.Name,
                RoomPatternProposalTool.Name,
            ],
            AssistantModelTools.Names);
        Assert.Equal(5, BimItAgentReadTools.Names.Length);
        Assert.Equal(8, AssistantTools.AllowedReadTools.Length);
        Assert.Equal(3, AssistantTools.WorkflowTools.Length);
        Assert.DoesNotContain(MepDevicePlacementProposalTool.Name, BimItAgentReadTools.Names);
        Assert.DoesNotContain(MepDevicePlacementProposalTool.Name, AssistantTools.AllowedReadTools);
        Assert.DoesNotContain(MepDevicePlacementProposalTool.Name, AssistantTools.WorkflowTools);

        string[] rawWrites =
        [
            "revit_preview_parameter_updates",
            "revit_request_parameter_update_review",
            "revit_apply_parameter_updates",
            "revit_preview_mep_device_placements",
            "revit_request_mep_device_placement_review",
            "revit_apply_mep_device_placements",
            "revit_preview_room_pattern_placements",
        ];
        foreach (var name in rawWrites)
        {
            Assert.Contains(name, BimItAgentReadTools.DeniedTools);
            Assert.True(AssistantTools.IsTrustedWriteTool(name));
            Assert.DoesNotContain(name, AssistantModelTools.Names);
            Assert.DoesNotContain(name, AssistantTools.AllowedReadTools);
            Assert.DoesNotContain(name, AssistantTools.WorkflowTools);
            Assert.False(AssistantModelTools.IsExposed(name));
            Assert.False(AssistantIpc.IsModelToolName(name));
        }

        var directory = Directory.CreateTempSubdirectory("bimit-proposal-surface-").FullName;
        try
        {
            var tools = GitHubCopilotSessionBlueprint.CreateTools(ReadTools(), ProposalTool(new MepDevicePlacementProposalStore()), PatternTool());
            Assert.Equal(AssistantModelTools.Names, tools.Select(tool => tool.Name).ToArray());
            var session = GitHubCopilotSessionBlueprint.CreateSessionConfig(directory, tools);
            var allowed = Assert.IsAssignableFrom<IList<string>>(session.AvailableTools);
            Assert.Equal(AssistantModelTools.Names.Select(name => "custom:" + name).ToArray(), allowed.ToArray());
            foreach (var name in rawWrites)
            {
                Assert.DoesNotContain(allowed, item => item.Contains(name, StringComparison.Ordinal));
            }

            var proposal = tools.Single(tool => tool.Name == MepDevicePlacementProposalTool.Name);
            var schema = proposal.JsonSchema.GetRawText();
            Assert.True(schema.Contains("\"length_unit\"", StringComparison.Ordinal), schema);
            Assert.Contains("\"placements\"", schema, StringComparison.Ordinal);
            Assert.Contains("\"offset_x\"", schema, StringComparison.Ordinal);
            Assert.Contains("\"offset_y\"", schema, StringComparison.Ordinal);
            Assert.Contains("\"vertical_offset\"", schema, StringComparison.Ordinal);
            Assert.Contains("\"source_evidence\"", schema, StringComparison.Ordinal);
            Assert.Contains("\"confidence\"", schema, StringComparison.Ordinal);
            foreach (var token in new[]
            {
                "instance_id",
                "document_id",
                "template_element_ref",
                "intent_ref",
                "element_ref",
                "artifact",
                "attachment",
                "\"state\"",
                "ready",
                "calibrated",
            })
            {
                Assert.DoesNotContain(token, schema, StringComparison.OrdinalIgnoreCase);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Validation_rejects_unit_range_count_duplicate_evidence_and_confidence_bounds()
    {
        var (tool, store, session) = Create(Selection(1, false, $"[\"{HiddenTemplate}\"]"));
        var accepted = await tool.InvokeAsync(Json(Placement("mm", Item(100_000, -100_000, 10_000, evidence: new string('e', 240), confidence: 0))), CancellationToken.None);
        var proposal = AssertNeedsCalibration(accepted, MepDevicePlacementProposalReasons.CoordinateBasisUnestablished);
        Assert.Equal("mm", proposal["length_unit"]);
        var row = Assert.Single(Rows(proposal));
        Assert.Equal(100_000d, row["offset_x"]);
        Assert.Equal(new string('e', 240), row["source_evidence"]);
        Assert.Equal(0d, row["confidence"]);
        Assert.Empty(session.Calls.Skip(1));

        Assert.True((bool)(await tool.InvokeAsync(Json(Placement("cm", Item(10_000, 0, -1_000))), CancellationToken.None))["ok"]!);
        Assert.True((bool)(await tool.InvokeAsync(Json(Placement("m", Item(100, -100, 10))), CancellationToken.None))["ok"]!);
        Assert.True((bool)(await tool.InvokeAsync(Json(Placement("in", Item(3937, 0, 393))), CancellationToken.None))["ok"]!);
        Assert.True((bool)(await tool.InvokeAsync(Json(Placement("ft", Item(328, -328, 32))), CancellationToken.None))["ok"]!);
        Assert.True((bool)(await tool.InvokeAsync(Json(Placement("m", Item(1, 2, 3, evidence: "  sheet note  ", confidence: 1))), CancellationToken.None))["ok"]!);
        var trimmed = Rows(AssertNeedsCalibration(
            await tool.InvokeAsync(Json(Placement("m", Item(1, 2, 3, evidence: "   ", confidence: 0.5))), CancellationToken.None),
            MepDevicePlacementProposalReasons.CoordinateBasisUnestablished));
        Assert.False(Assert.Single(trimmed).ContainsKey("source_evidence"));
        Assert.Equal(0.5d, Assert.Single(trimmed)["confidence"]);

        var twenty = string.Join(",", Enumerable.Range(0, 20).Select(index => Item(index, 0, 0)));
        Assert.True((bool)(await tool.InvokeAsync(Json(Placement("m", twenty)), CancellationToken.None))["ok"]!);
        Assert.Equal(20, store.Current!.Placements.Count);

        await AssertRejected(tool, Placement("px", Item(1, 0, 0)), MepDevicePlacementProposalFailures.Unit);
        await AssertRejected(tool, Placement("FT", Item(1, 0, 0)), MepDevicePlacementProposalFailures.Unit);
        await AssertRejected(tool, Placement("", Item(1, 0, 0)), MepDevicePlacementProposalFailures.Unit);
        await AssertRejected(tool, "{\"length_unit\":1,\"placements\":[" + Item(1, 0, 0) + "]}", MepDevicePlacementProposalFailures.Shape);
        await AssertRejected(tool, "{\"length_unit\":\"m\"}", MepDevicePlacementProposalFailures.Shape);
        await AssertRejected(tool, Placement("m", ""), MepDevicePlacementProposalFailures.Count);
        await AssertRejected(tool, Placement("m", string.Join(",", Enumerable.Range(0, 21).Select(index => Item(index, 0, 0)))), MepDevicePlacementProposalFailures.Count);
        await AssertRejected(tool, Placement("mm", Item(100_001, 0, 0)), MepDevicePlacementProposalFailures.Range);
        await AssertRejected(tool, Placement("cm", Item(0, 10_001, 0)), MepDevicePlacementProposalFailures.Range);
        await AssertRejected(tool, Placement("m", Item(0, 0, 10.001)), MepDevicePlacementProposalFailures.Range);
        await AssertRejected(tool, Placement("in", Item(3938, 0, 0)), MepDevicePlacementProposalFailures.Range);
        await AssertRejected(tool, Placement("ft", Item(0, 329, 0)), MepDevicePlacementProposalFailures.Range);
        await AssertRejected(tool, Placement("m", Item(1, 0, 0) + "," + Item(1, 0, 0)), MepDevicePlacementProposalFailures.Duplicate);
        await AssertRejected(tool, Placement("m", "{\"offset_x\":\"1\",\"offset_y\":0,\"vertical_offset\":0}"), MepDevicePlacementProposalFailures.Shape);
        await AssertRejected(tool, Placement("m", "{\"offset_x\":1e309,\"offset_y\":0,\"vertical_offset\":0}"), MepDevicePlacementProposalFailures.NonFinite);
        AssertInvalid(
            await tool.InvokeRequestAsync(
                new MepDevicePlacementProposalRequest
                {
                    LengthUnit = "m",
                    Placements =
                    [
                        new MepDevicePlacementProposalItemRequest
                        {
                            OffsetX = double.NaN,
                            OffsetY = 0,
                            VerticalOffset = 0,
                        },
                    ],
                },
                CancellationToken.None),
            MepDevicePlacementProposalFailures.NonFinite);
        await AssertRejected(tool, Placement("m", Item(1, 0, 0, evidence: new string('e', 241))), MepDevicePlacementProposalFailures.Evidence);
        await AssertRejected(tool, Placement("m", Item(1, 0, 0, confidence: -0.01)), MepDevicePlacementProposalFailures.Confidence);
        await AssertRejected(tool, Placement("m", Item(1, 0, 0, confidence: 1.01)), MepDevicePlacementProposalFailures.Confidence);
        await AssertRejected(tool, Placement("m", "{\"offset_x\":1,\"offset_y\":0,\"vertical_offset\":0,\"confidence\":true}"), MepDevicePlacementProposalFailures.Shape);
        Assert.Equal(20, store.Current.Placements.Count);
        Assert.DoesNotContain(session.Calls, call => call.Tool != AssistantTools.GetSelection);
    }

    [Fact]
    public async Task Valid_proposal_replaces_the_previous_and_invalid_input_preserves_it()
    {
        var (tool, store, session) = Create(Selection(0, false, "[]"));
        var first = await tool.InvokeAsync(Json(Placement("m", Item(1, 2, 3))), CancellationToken.None);
        Assert.Equal(1d, Assert.Single(Rows(AssertNeedsCalibration(first, MepDevicePlacementProposalReasons.TemplateSelectionRequired)))["offset_x"]);
        var kept = store.Current;

        var second = await tool.InvokeAsync(Json(Placement("mm", Item(4, 5, 6) + "," + Item(7, 8, 9))), CancellationToken.None);
        Assert.Equal(2, Rows(AssertNeedsCalibration(second, MepDevicePlacementProposalReasons.TemplateSelectionRequired)).Count);
        Assert.NotSame(kept, store.Current);
        Assert.Equal("mm", store.Current!.LengthUnit);

        var preserved = store.Current;
        var rejected = await tool.InvokeAsync(Json(Placement("m", Item(1, 2, 3, extra: "\"instance_id\":\"evil\""))), CancellationToken.None);
        AssertInvalid(rejected, MepDevicePlacementProposalFailures.Shape);
        Assert.Same(preserved, store.Current);
        Assert.Equal("mm", store.Current!.LengthUnit);
        Assert.Equal(2, store.Current.Placements.Count);
        Assert.Equal(
            [MepDevicePlacementProposalTool.Name, MepDevicePlacementProposalTool.Name, MepDevicePlacementProposalTool.Name],
            tool.ConsumeUsedTools());
        Assert.Equal(2, session.Calls.Count);
    }

    [Fact]
    public async Task Exact_template_is_bound_in_hidden_state_and_stays_needs_calibration()
    {
        var (tool, store, session) = Create(Selection(1, false, $"[\"{HiddenTemplate}\"]"));
        var result = await tool.InvokeAsync(
            Json(Placement("m", Item(1, 2, 3, evidence: "project axes are calibrated", confidence: 1))),
            CancellationToken.None);
        var proposal = AssertNeedsCalibration(result, MepDevicePlacementProposalReasons.CoordinateBasisUnestablished);
        Assert.Equal("project axes are calibrated", Assert.Single(Rows(proposal))["source_evidence"]);
        Assert.Equal(1d, Assert.Single(Rows(proposal))["confidence"]);
        AssertHidden(result, HiddenDocument, HiddenTemplate, InstanceId, "wire-instance");

        var call = Assert.Single(session.Calls);
        Assert.Equal(AssistantTools.GetSelection, call.Tool);
        Assert.Equal(InstanceId, call.Arguments["instance_id"]);
        Assert.Equal(MepDevicePlacementProposalTool.SelectionProbeLimit, call.Arguments["limit"]);
        Assert.False(call.Arguments.ContainsKey("document_id"));
        Assert.Equal(HiddenDocument, store.Current!.DocumentId);
        Assert.Equal(HiddenTemplate, store.Current.TemplateElementRef);
        Assert.Equal(InstanceId, store.Current.InstanceId);
        Assert.Equal(MepDevicePlacementProposalStates.NeedsCalibration, store.Current.State);
        Assert.Null(store.Current.SourceArtifactId);

        Assert.Throws<ArgumentNullException>(() => store.TryPromoteToReady(null!));
        Assert.Equal(MepDevicePlacementProposalStates.NeedsCalibration, store.Current.State);
        Assert.True(store.TryPromoteToReady(new TrustedProjectAxisCalibration()));
        Assert.Equal(MepDevicePlacementProposalStates.Ready, store.Current.State);
        Assert.Null(store.Current.Reason);
        var promoted = store.Current.ToModelResult();
        Assert.Equal(MepDevicePlacementProposalStates.Ready, Assert.IsType<Dictionary<string, object?>>(promoted["proposal"])["state"]);
        AssertHidden(promoted, HiddenDocument, HiddenTemplate, InstanceId);

        var unbound = new MepDevicePlacementProposalStore();
        unbound.Replace(MepDevicePlacementProposal.Create(
            "m",
            [new MepDevicePlacementProposalItem(1, 0, 0, null, null)],
            MepDevicePlacementProposalReasons.TemplateSelectionRequired,
            InstanceId,
            null,
            null,
            null));
        Assert.False(unbound.TryPromoteToReady(new TrustedProjectAxisCalibration()));
        Assert.Equal(MepDevicePlacementProposalStates.NeedsCalibration, unbound.Current!.State);

        var sourced = Create(Selection(1, false, $"[\"{HiddenTemplate}\"]"), "artifact-hidden");
        var sourcedResult = await sourced.Tool.InvokeAsync(Json(Placement("m", Item(8, 0, 0))), CancellationToken.None);
        AssertNeedsCalibration(sourcedResult, MepDevicePlacementProposalReasons.CoordinateBasisUnestablished);
        Assert.Equal("artifact-hidden", sourced.Store.Current!.SourceArtifactId);
        AssertHidden(sourcedResult, "artifact-hidden", HiddenDocument, HiddenTemplate);
    }

    [Fact]
    public async Task Missing_or_multiple_templates_and_no_document_stay_needs_calibration()
    {
        var none = Create(Selection(0, false, "[]"));
        var empty = await none.Tool.InvokeAsync(Json(Placement("m", Item(1, 0, 0))), CancellationToken.None);
        AssertNeedsCalibration(empty, MepDevicePlacementProposalReasons.TemplateSelectionRequired);
        Assert.Null(none.Store.Current!.DocumentId);
        Assert.Null(none.Store.Current.TemplateElementRef);
        AssertHidden(empty, HiddenDocument, InstanceId);

        var many = Create(Selection(3, true, "[\"ref-a\",\"ref-b\"]"));
        var multiple = await many.Tool.InvokeAsync(Json(Placement("m", Item(2, 0, 0))), CancellationToken.None);
        AssertNeedsCalibration(multiple, MepDevicePlacementProposalReasons.TemplateSelectionRequired);
        Assert.Null(many.Store.Current!.TemplateElementRef);
        AssertHidden(multiple, "ref-a", "ref-b", HiddenDocument);

        var missingDocument = Create(_ => Error(CapabilityErrorCodes.NoActiveDocument, "No active Revit document is available. C:\\secret\\model.rvt"));
        var noDocument = await missingDocument.Tool.InvokeAsync(Json(Placement("m", Item(3, 0, 0))), CancellationToken.None);
        AssertNeedsCalibration(noDocument, MepDevicePlacementProposalReasons.NoActiveDocument);
        Assert.Null(missingDocument.Store.Current!.DocumentId);
        AssertHidden(noDocument, "C:\\secret", "model.rvt");
        Assert.DoesNotContain("secret", JsonSerializer.Serialize(noDocument), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Model_cannot_set_state_or_identity_and_claims_do_not_make_a_proposal_ready()
    {
        var (tool, store, session) = Create(Selection(1, false, $"[\"{HiddenTemplate}\"]"));
        await tool.InvokeAsync(Json(Placement("m", Item(1, 0, 0))), CancellationToken.None);
        var kept = store.Current;

        await AssertRejected(tool, Placement("m", Item(4, 0, 0)).TrimEnd('}') + ",\"state\":\"ready\"}", MepDevicePlacementProposalFailures.Shape);
        await AssertRejected(tool, Placement("m", Item(4, 0, 0)).TrimEnd('}') + ",\"calibrated\":true}", MepDevicePlacementProposalFailures.Shape);
        await AssertRejected(tool, Placement("m", Item(4, 0, 0)).TrimEnd('}') + ",\"document_id\":\"doc-evil\"}", MepDevicePlacementProposalFailures.Shape);
        await AssertRejected(tool, Placement("m", Item(4, 0, 0)).TrimEnd('}') + ",\"template_element_ref\":\"ref-evil\"}", MepDevicePlacementProposalFailures.Shape);
        await AssertRejected(tool, Placement("m", Item(4, 0, 0)).TrimEnd('}') + ",\"intent_ref\":\"intent-evil\"}", MepDevicePlacementProposalFailures.Shape);
        await AssertRejected(tool, Placement("m", Item(4, 0, 0)).TrimEnd('}') + ",\"artifact_id\":\"artifact-evil\"}", MepDevicePlacementProposalFailures.Shape);
        Assert.Same(kept, store.Current);
        Assert.Equal(MepDevicePlacementProposalStates.NeedsCalibration, store.Current!.State);
        Assert.Single(session.Calls);

        var claimed = await tool.InvokeAsync(
            Json(Placement("m", Item(9, 0, 0, evidence: "calibrated true on project axes"))),
            CancellationToken.None);
        AssertNeedsCalibration(claimed, MepDevicePlacementProposalReasons.CoordinateBasisUnestablished);
        Assert.Equal(MepDevicePlacementProposalStates.NeedsCalibration, store.Current.State);
        Assert.NotSame(kept, store.Current);
    }

    [Fact]
    public async Task Attachment_replacement_removal_new_chat_and_shutdown_follow_the_source_rule()
    {
        var directory = Directory.CreateTempSubdirectory("bimit-proposal-attachment-").FullName;
        var first = Path.Combine(directory, "one.txt");
        var second = Path.Combine(directory, "two.txt");
        await File.WriteAllTextAsync(first, "one");
        await File.WriteAllTextAsync(second, "two");
        var store = new MepDevicePlacementProposalStore();
        var runtime = new RecordingRuntime();
        var pipe = AssistantPipeNames.Create(Environment.ProcessId, Guid.NewGuid().ToString("N"));
        var host = AssistantHost.RunAsync(
            new AssistantHostOptions(pipe, InstanceId, null, "2026", "26.5.0.55"),
            new IdleSession(),
            runtime,
            providerTimeouts: new AssistantProviderTimeouts(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2)),
            proposals: store);
        await using var client = AssistantPipeStreams.CreateClient(pipe);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await client.ConnectAsync(5_000, timeout.Token);
        await AssistantPipeFrames.WriteAsync(client, AssistantIpc.Serialize(Hello()), timeout.Token);
        _ = await AssistantPipeFrames.ReadAsync(client, timeout.Token);

        var unbound = Sample(null);
        store.Replace(unbound);
        await AttachAsync(client, first, timeout.Token);
        Assert.Same(unbound, store.Current);
        await RemoveAsync(client, timeout.Token);
        Assert.Same(unbound, store.Current);

        await AttachAsync(client, first, timeout.Token);
        await ChatAsync(client, "read", timeout.Token);
        var sourceId = runtime.Requests[^1].Attachment!.ArtifactId;
        var bound = Sample(sourceId);
        store.Replace(bound);
        await AttachAsync(client, Path.Combine(directory, "missing.txt"), timeout.Token);
        Assert.Same(bound, store.Current);

        await AttachAsync(client, second, timeout.Token);
        Assert.Null(store.Current);
        await ChatAsync(client, "again", timeout.Token);
        var replacedId = runtime.Requests[^1].Attachment!.ArtifactId;
        Assert.NotEqual(sourceId, replacedId);
        store.Replace(Sample(replacedId));
        await RemoveAsync(client, timeout.Token);
        Assert.Null(store.Current);

        store.Replace(Sample(null));
        await AssistantPipeFrames.WriteAsync(
            client,
            AssistantIpc.Serialize(new AssistantIpcMessage(AssistantIpcKind.ResetConversation, null, null, null, false, null, null, null, null, null, null, null, null, null, false)),
            timeout.Token);
        _ = await AssistantPipeFrames.ReadAsync(client, timeout.Token);
        Assert.Null(store.Current);

        store.Replace(Sample(null));
        await AssistantPipeFrames.WriteAsync(
            client,
            AssistantIpc.Serialize(new AssistantIpcMessage(AssistantIpcKind.Shutdown, null, null, null, false, null, null, null, null, null, null, null, null, null, false)),
            timeout.Token);
        Assert.Equal(0, await host);
        Assert.Null(store.Current);
    }

    [Fact]
    public void Used_tools_accepts_the_proposal_tool_and_rejects_raw_writes()
    {
        var accepted = AssistantIpc.Parse("""{"type":"chatResult","requestId":"abc","title":"BIM-IT Assistant","body":"x","usedTools":["bimit_propose_mep_device_placements"]}""");
        Assert.True(accepted.Accepted);
        Assert.Equal([MepDevicePlacementProposalTool.Name], accepted.Message!.UsedTools);
        Assert.True(AssistantIpc.IsModelToolName(MepDevicePlacementProposalTool.Name));
        foreach (var name in new[]
        {
            "revit_preview_parameter_updates",
            "revit_request_parameter_update_review",
            "revit_apply_parameter_updates",
            "revit_preview_mep_device_placements",
            "revit_request_mep_device_placement_review",
            "revit_apply_mep_device_placements",
        })
        {
            Assert.False(AssistantIpc.Parse("{\"type\":\"chatResult\",\"requestId\":\"abc\",\"title\":\"BIM-IT Assistant\",\"body\":\"x\",\"usedTools\":[\"" + name + "\"]}").Accepted);
        }
    }

    private static async Task AssertRejected(MepDevicePlacementProposalTool tool, string json, string reason)
    {
        AssertInvalid(await tool.InvokeAsync(Json(json), CancellationToken.None), reason);
    }

    private static void AssertInvalid(IReadOnlyDictionary<string, object?> result, string reason)
    {
        Assert.False((bool)result["ok"]!);
        Assert.Equal(MepDevicePlacementProposalFailures.Code, result["code"]);
        Assert.Equal(reason, result["reason"]);
        Assert.Equal("The placement proposal is invalid.", result["message"]);
    }

    private static Dictionary<string, object?> AssertNeedsCalibration(IReadOnlyDictionary<string, object?> result, string reason)
    {
        Assert.True((bool)result["ok"]!);
        var proposal = Assert.IsType<Dictionary<string, object?>>(result["proposal"]);
        Assert.Equal(MepDevicePlacementProposalStates.NeedsCalibration, proposal["state"]);
        Assert.Equal(reason, proposal["reason"]);
        return proposal;
    }

    private static List<Dictionary<string, object?>> Rows(Dictionary<string, object?> proposal)
    {
        return Assert.IsAssignableFrom<IEnumerable<Dictionary<string, object?>>>(proposal["placements"]).ToList();
    }

    private static void AssertHidden(object? value, params string[] secrets)
    {
        switch (value)
        {
            case IReadOnlyDictionary<string, object?> map:
                foreach (var pair in map)
                {
                    Assert.DoesNotContain(pair.Key, new[]
                    {
                        "instance_id",
                        "document_id",
                        "template_element_ref",
                        "intent_ref",
                        "element_ref",
                        "artifact_id",
                        "attachment_id",
                        "calibrated",
                        "ready",
                    });
                    AssertHidden(pair.Value, secrets);
                }

                break;
            case string text:
                foreach (var secret in secrets)
                {
                    Assert.DoesNotContain(secret, text, StringComparison.Ordinal);
                }

                break;
            case System.Collections.IEnumerable enumerable:
                foreach (var item in enumerable)
                {
                    AssertHidden(item, secrets);
                }

                break;
        }
    }

    private static (MepDevicePlacementProposalTool Tool, MepDevicePlacementProposalStore Store, RecordingSession Session) Create(
        Func<string, AssistantToolCall> responder,
        string? attachmentId = null)
    {
        var session = new RecordingSession(responder);
        var store = new MepDevicePlacementProposalStore();
        var tool = new MepDevicePlacementProposalTool(session, InstanceId, store, () => attachmentId);
        return (tool, store, session);
    }

    private static BimItAgentReadTools ReadTools()
    {
        return new BimItAgentReadTools(new RecordingSession(_ => Error("unused", "unused")), InstanceId);
    }

    private static MepDevicePlacementProposalTool ProposalTool(MepDevicePlacementProposalStore store)
    {
        return new MepDevicePlacementProposalTool(new RecordingSession(_ => Error("unused", "unused")), InstanceId, store, static () => null);
    }

    private static RoomPatternProposalTool PatternTool()
    {
        return new RoomPatternProposalTool(new RecordingSession(_ => Error("unused", "unused")), InstanceId, new RoomPatternStore(), static () => null);
    }

    private static Func<string, AssistantToolCall> Selection(int count, bool truncated, string refs)
    {
        var document = $$"""
            {
              "context": { "document_id": "{{HiddenDocument}}", "instance_id": "wire-instance" },
              "selected_count": {{count}},
              "truncated": {{(truncated ? "true" : "false")}},
              "element_refs": {{refs}}
            }
            """;
        return tool =>
        {
            Assert.Equal(AssistantTools.GetSelection, tool);
            return Success(document);
        };
    }

    private static MepDevicePlacementProposal Sample(string? sourceArtifactId)
    {
        return MepDevicePlacementProposal.Create(
            "mm",
            [new MepDevicePlacementProposalItem(10, 0, 0, null, null)],
            MepDevicePlacementProposalReasons.CoordinateBasisUnestablished,
            InstanceId,
            null,
            null,
            sourceArtifactId);
    }

    private static string Placement(string unit, string items)
    {
        return "{\"length_unit\":\"" + unit + "\",\"placements\":[" + items + "]}";
    }

    private static string Item(double x, double y, double z, string? evidence = null, double? confidence = null, string? extra = null)
    {
        var json = FormattableString.Invariant($"{{\"offset_x\":{x},\"offset_y\":{y},\"vertical_offset\":{z}");
        if (evidence is not null)
        {
            json += ",\"source_evidence\":\"" + evidence + "\"";
        }

        if (confidence is not null)
        {
            json += FormattableString.Invariant($",\"confidence\":{confidence.Value}");
        }

        if (extra is not null)
        {
            json += "," + extra;
        }

        return json + "}";
    }

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
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

    private static AssistantIpcMessage Hello()
    {
        return new AssistantIpcMessage(AssistantIpcKind.Hello, InstanceId, "2026", "26.5.0.55", true, null, null, null, null, null, null, null, null, null, false);
    }

    private static async Task AttachAsync(NamedPipeClientStream client, string path, CancellationToken cancellationToken)
    {
        await AssistantPipeFrames.WriteAsync(
            client,
            AssistantIpc.Serialize(new AssistantIpcMessage(
                AssistantIpcKind.AttachDocument, null, null, null, false, null, null, null, null, null, null, null, null, null, false, DocumentPath: path)),
            cancellationToken);
        _ = await AssistantPipeFrames.ReadAsync(client, cancellationToken);
    }

    private static async Task RemoveAsync(NamedPipeClientStream client, CancellationToken cancellationToken)
    {
        await AssistantPipeFrames.WriteAsync(
            client,
            AssistantIpc.Serialize(new AssistantIpcMessage(AssistantIpcKind.RemoveDocument, null, null, null, false, null, null, null, null, null, null, null, null, null, false)),
            cancellationToken);
        _ = await AssistantPipeFrames.ReadAsync(client, cancellationToken);
    }

    private static async Task ChatAsync(NamedPipeClientStream client, string text, CancellationToken cancellationToken)
    {
        await AssistantPipeFrames.WriteAsync(
            client,
            AssistantIpc.Serialize(new AssistantIpcMessage(
                AssistantIpcKind.Chat, null, null, null, false, "chat-1", null, null, null, null, null, null, null, null, false, Text: text)),
            cancellationToken);
        _ = await AssistantPipeFrames.ReadAsync(client, cancellationToken);
    }

    private sealed class RecordingSession(Func<string, AssistantToolCall> responder) : IAssistantToolSession
    {
        public List<Call> Calls { get; } = [];

        public Task<AssistantMcpReadiness> InitializeAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new AssistantMcpReadiness(true, 14, [], null));
        }

        public Task<AssistantToolCall> CallAsync(string toolName, IReadOnlyDictionary<string, object?> arguments, CancellationToken cancellationToken)
        {
            Calls.Add(new Call(toolName, new Dictionary<string, object?>(arguments)));
            return Task.FromResult(responder(toolName));
        }

        public sealed record Call(string Tool, Dictionary<string, object?> Arguments);
    }

    private sealed class IdleSession : IAssistantToolSession
    {
        public Task<AssistantMcpReadiness> InitializeAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new AssistantMcpReadiness(true, 14, [], null));
        }

        public Task<AssistantToolCall> CallAsync(string toolName, IReadOnlyDictionary<string, object?> arguments, CancellationToken cancellationToken)
        {
            return Task.FromResult(new AssistantToolCall(true, "unused", "unused", null));
        }
    }

    private sealed class RecordingRuntime : IAssistantAgentRuntime
    {
        public List<AssistantAgentRequest> Requests { get; } = [];

        public AssistantProviderStatus Status { get; private set; } = new("GitHub Copilot", AssistantProviderState.Ready, "auto", null);

        public Task InitializeAsync(CancellationToken cancellationToken)
        {
            Status = new AssistantProviderStatus("GitHub Copilot", AssistantProviderState.Ready, "auto", null);
            return Task.CompletedTask;
        }

        public Task<AssistantAgentTurn> SendAsync(AssistantAgentRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new AssistantAgentTurn("BIM-IT Assistant", "Noted.", false));
        }

        public Task ResetConversationAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
