using System.Text.Json;
using BimIt.Revit.Assistant.Ipc;
using BimIt.Revit.Contracts;
using Xunit;

namespace BimIt.Revit.Assistant.Tests;

public sealed class AssistantPreviewOrchestrationTests
{
    private const string InstanceId = "inst-secret-1";
    private const string DocumentId = "doc-secret-9f3a";
    private const string TemplateRef = "ref-secret-77";
    private const string IntentRef = "intent-secret-abc";
    private const string Fingerprint = "fp-secret-xyz";

    [Fact]
    public void Local_action_is_not_a_model_tool_and_approval_authority_stays_false()
    {
        Assert.Equal(8, AssistantModelTools.Names.Length);
        Assert.False(AssistantModelTools.IsExposed("confirmProjectAxesPreview"));
        Assert.False(AssistantIpc.IsModelToolName("confirmProjectAxesPreview"));
        Assert.False(AssistantIpc.IsModelToolName(IMepDevicePlacementPreviewClient.ToolName));
        Assert.False(AssistantIpc.IsModelToolName("revit_request_mep_device_placement_review"));
        Assert.False(AssistantIpc.IsModelToolName("revit_apply_mep_device_placements"));
        Assert.False(AssistantIpc.GrantsApprovalAuthority);
        Assert.Equal(9, AssistantTools.AllowedReadTools.Length);
        Assert.Equal(3, AssistantTools.WorkflowTools.Length);
        Assert.DoesNotContain(IMepDevicePlacementPreviewClient.ToolName, AssistantTools.AllowedReadTools);
        Assert.True(AssistantTools.IsTrustedWriteTool(IMepDevicePlacementPreviewClient.ToolName));
        Assert.True(AssistantTools.IsTrustedWriteTool("revit_request_mep_device_placement_review"));
        Assert.True(AssistantTools.IsTrustedWriteTool("revit_apply_mep_device_placements"));

        var signal = AssistantIpc.Parse("""{"type":"confirmProjectAxesPreview"}""");
        Assert.Equal(AssistantIpcKind.ConfirmProjectAxesPreview, signal.Message!.Kind);
        Assert.False(AssistantIpc.Parse("""{"type":"confirmProjectAxesPreview","text":"ready"}""").Accepted);
    }

    [Theory]
    [InlineData("mm", "autodesk.unit.unit:millimeters-1.0.1")]
    [InlineData("cm", "autodesk.unit.unit:centimeters-1.0.1")]
    [InlineData("m", "autodesk.unit.unit:meters-1.0.1")]
    [InlineData("in", "autodesk.unit.unit:inches-1.0.1")]
    [InlineData("ft", "autodesk.unit.unit:feet-1.0.1")]
    public async Task Unit_mapping_and_hidden_identities_are_injected_only_into_the_preview_request(string unit, string forgeTypeId)
    {
        var now = new DateTimeOffset(2026, 10, 19, 18, 0, 0, TimeSpan.Zero);
        var (orchestrator, store, session, preview) = Create(unit, now);
        preview.Result = ReadyResult(now.AddMinutes(5));

        var outcome = await orchestrator.ConfirmAsync(CancellationToken.None);

        Assert.False(outcome.IsError);
        Assert.Equal(MepDevicePlacementProposalStates.Ready, store.Current!.State);
        Assert.Equal(AssistantTools.GetSelection, Assert.Single(session.Tools));
        var request = Assert.Single(preview.Requests);
        Assert.Equal(InstanceId, preview.InstanceId);
        Assert.Equal(DocumentId, request.DocumentId);
        Assert.Equal(TemplateRef, request.TemplateElementRef);
        Assert.Equal(forgeTypeId, request.LengthUnitId);
        Assert.Equal(2, request.Placements.Count);
        Assert.Equal(1000, request.Placements[0].OffsetX);
        AssertHidden(outcome.Body);
        Assert.DoesNotContain(forgeTypeId, outcome.Body, StringComparison.Ordinal);
        Assert.Contains("Preview ready: yes", outcome.Body, StringComparison.Ordinal);
        Assert.Contains("Duplex Receptacle", outcome.Body, StringComparison.Ordinal);
        Assert.Contains("Level-based", outcome.Body, StringComparison.Ordinal);
        Assert.Equal(IntentRef, store.Preview!.IntentRef);
        Assert.Equal(Fingerprint, store.Preview.Fingerprint);
    }

    [Fact]
    public async Task Changed_selection_fails_closed_without_preview_or_rebind()
    {
        var now = new DateTimeOffset(2026, 10, 19, 18, 0, 0, TimeSpan.Zero);
        var (orchestrator, store, session, preview) = Create("mm", now);
        session.SelectionJson = Selection(0, false, "[]");
        var before = store.Current;

        var outcome = await orchestrator.ConfirmAsync(CancellationToken.None);

        Assert.True(outcome.IsError);
        Assert.Contains("Create a new proposal", outcome.Body, StringComparison.Ordinal);
        Assert.Same(before, store.Current);
        Assert.Equal(DocumentId, store.Current!.DocumentId);
        Assert.Equal(TemplateRef, store.Current.TemplateElementRef);
        Assert.Equal(MepDevicePlacementProposalStates.NeedsCalibration, store.Current.State);
        Assert.Empty(preview.Requests);
        Assert.Null(store.Preview);
    }

    [Fact]
    public async Task Repeated_action_reuses_an_unexpired_preview_and_recreates_an_expired_one()
    {
        var now = new DateTimeOffset(2026, 10, 19, 18, 0, 0, TimeSpan.Zero);
        var clock = now;
        var (orchestrator, store, _, preview) = Create("mm", () => clock);
        preview.Result = ReadyResult(now.AddMinutes(5));

        var first = await orchestrator.ConfirmAsync(CancellationToken.None);
        var second = await orchestrator.ConfirmAsync(CancellationToken.None);

        Assert.Equal(first.Body, second.Body);
        Assert.Single(preview.Requests);

        clock = now.AddMinutes(5);
        preview.Result = ReadyResult(clock.AddMinutes(5), "intent-secret-next");
        var third = await orchestrator.ConfirmAsync(CancellationToken.None);

        Assert.Equal(2, preview.Requests.Count);
        Assert.Contains("Preview ready: yes", third.Body, StringComparison.Ordinal);
        Assert.Equal("intent-secret-next", store.Preview!.IntentRef);
        Assert.DoesNotContain("intent-secret-next", third.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Not_ready_preview_keeps_calibration_without_a_receipt()
    {
        var now = new DateTimeOffset(2026, 10, 19, 18, 0, 0, TimeSpan.Zero);
        var (orchestrator, store, _, preview) = Create("mm", now);
        preview.Result = NotReadyResult();

        var outcome = await orchestrator.ConfirmAsync(CancellationToken.None);

        Assert.False(outcome.IsError);
        Assert.Equal(MepDevicePlacementProposalStates.Ready, store.Current!.State);
        Assert.Null(store.Preview);
        Assert.Contains("Preview ready: no", outcome.Body, StringComparison.Ordinal);
        Assert.Contains("unsupported category", outcome.Body, StringComparison.Ordinal);
        Assert.Contains("unsupported position", outcome.Body, StringComparison.Ordinal);
        AssertHidden(outcome.Body);
    }

    [Theory]
    [InlineData("instance")]
    [InlineData("document")]
    [InlineData("template")]
    [InlineData("count")]
    [InlineData("offsets")]
    public async Task Mismatched_preview_result_fails_closed_without_a_receipt(string mismatch)
    {
        var now = new DateTimeOffset(2026, 10, 19, 18, 0, 0, TimeSpan.Zero);
        var (orchestrator, store, _, preview) = Create("mm", now);
        store.RememberPreview("intent-secret-old", Fingerprint, now, "Placements: 2");
        preview.Result = Mismatch(mismatch, now.AddMinutes(5));

        var outcome = await orchestrator.ConfirmAsync(CancellationToken.None);

        Assert.True(outcome.IsError);
        Assert.Equal("The placement preview could not be created.", outcome.Body);
        Assert.Equal(MepDevicePlacementProposalStates.Ready, store.Current!.State);
        Assert.Equal(DocumentId, store.Current.DocumentId);
        Assert.Equal(TemplateRef, store.Current.TemplateElementRef);
        Assert.Null(store.Preview);
        Assert.Single(preview.Requests);
        AssertHidden(outcome.Body);
    }

    [Theory]
    [InlineData("template")]
    [InlineData("item")]
    [InlineData("intent")]
    [InlineData("fingerprint")]
    [InlineData("expiry")]
    public async Task Malformed_ready_preview_is_not_stored(string defect)
    {
        var now = new DateTimeOffset(2026, 10, 19, 18, 0, 0, TimeSpan.Zero);
        var (orchestrator, store, _, preview) = Create("mm", now);
        store.RememberPreview("intent-secret-old", Fingerprint, now, "Placements: 2");
        preview.Result = MalformedReady(defect, now.AddMinutes(5));

        var outcome = await orchestrator.ConfirmAsync(CancellationToken.None);

        Assert.True(outcome.IsError);
        Assert.Equal("The placement preview could not be created.", outcome.Body);
        Assert.Equal(MepDevicePlacementProposalStates.Ready, store.Current!.State);
        Assert.Null(store.Preview);
        Assert.DoesNotContain("Preview ready:", outcome.Body, StringComparison.Ordinal);
        AssertHidden(outcome.Body);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Expired_ready_intent_is_not_stored(int minutesFromNow)
    {
        var now = new DateTimeOffset(2026, 10, 19, 18, 0, 0, TimeSpan.Zero);
        var (orchestrator, store, _, preview) = Create("mm", now);
        store.RememberPreview("intent-secret-old", Fingerprint, now, "Placements: 2");
        preview.Result = ReadyResult(now.AddMinutes(minutesFromNow));

        var outcome = await orchestrator.ConfirmAsync(CancellationToken.None);

        Assert.True(outcome.IsError);
        Assert.Equal("The placement preview could not be created.", outcome.Body);
        Assert.Equal(MepDevicePlacementProposalStates.Ready, store.Current!.State);
        Assert.Null(store.Preview);
        AssertHidden(outcome.Body);
    }

    [Fact]
    public async Task Not_ready_preview_with_stray_intent_fields_is_not_actionable()
    {
        var now = new DateTimeOffset(2026, 10, 19, 18, 0, 0, TimeSpan.Zero);
        var (orchestrator, store, _, preview) = Create("mm", now);
        store.RememberPreview("intent-secret-old", Fingerprint, now, "Placements: 2");
        var notReady = NotReadyResult();
        preview.Result = new PreviewMepDevicePlacementsResult
        {
            Context = notReady.Context,
            Ready = false,
            Template = notReady.Template,
            Items = notReady.Items,
            IntentRef = IntentRef,
            IntentFingerprint = Fingerprint,
            ExpiresAt = now.AddMinutes(5),
        };

        var outcome = await orchestrator.ConfirmAsync(CancellationToken.None);

        Assert.False(outcome.IsError);
        Assert.Equal(MepDevicePlacementProposalStates.Ready, store.Current!.State);
        Assert.Null(store.Preview);
        Assert.Contains("Preview ready: no", outcome.Body, StringComparison.Ordinal);
        Assert.Contains("unsupported category", outcome.Body, StringComparison.Ordinal);
        Assert.Contains("unsupported position", outcome.Body, StringComparison.Ordinal);
        AssertHidden(outcome.Body);
        Assert.DoesNotContain("intent-secret-old", outcome.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void New_proposal_attachment_invalidation_and_clear_drop_the_hidden_preview()
    {
        var store = new MepDevicePlacementProposalStore();
        store.Replace(Bound("mm", "artifact-hidden"));
        store.RememberPreview(IntentRef, Fingerprint, DateTimeOffset.UtcNow.AddMinutes(5), "Placements: 1");
        Assert.NotNull(store.Preview);

        store.Replace(Bound("mm", "artifact-hidden"));
        Assert.Null(store.Preview);

        store.RememberPreview(IntentRef, Fingerprint, DateTimeOffset.UtcNow.AddMinutes(5), "Placements: 1");
        store.NoteAttachmentIdentityChanged("artifact-hidden", "artifact-next");
        Assert.Null(store.Current);
        Assert.Null(store.Preview);

        store.Replace(Bound("mm", null));
        store.RememberPreview(IntentRef, Fingerprint, DateTimeOffset.UtcNow.AddMinutes(5), "Placements: 1");
        store.Clear();
        Assert.Null(store.Current);
        Assert.Null(store.Preview);
    }

    [Fact]
    public async Task Chat_text_does_not_confirm_axes_and_the_local_signal_does()
    {
        var store = new MepDevicePlacementProposalStore();
        store.Replace(Bound("mm", null));
        var session = new SelectionSession(Selection(1, false, "[\"" + TemplateRef + "\"]"));
        var preview = new RecordingPreview { Result = ReadyResult(DateTimeOffset.UtcNow.AddMinutes(5)) };
        var runtime = new IdleRuntime();
        var pipe = AssistantPipeNames.Create(Environment.ProcessId, Guid.NewGuid().ToString("N"));
        var host = AssistantHost.RunAsync(
            new AssistantHostOptions(pipe, InstanceId, null, "2026", "26.5.0.55"),
            session,
            runtime,
            providerTimeouts: new AssistantProviderTimeouts(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2)),
            proposals: store,
            previewClient: preview);
        await using var client = AssistantPipeStreams.CreateClient(pipe);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await client.ConnectAsync(5_000, timeout.Token);
        await AssistantPipeFrames.WriteAsync(client, AssistantIpc.Serialize(Hello()), timeout.Token);
        _ = await AssistantPipeFrames.ReadAsync(client, timeout.Token);

        await AssistantPipeFrames.WriteAsync(
            client,
            AssistantIpc.Serialize(new AssistantIpcMessage(
                AssistantIpcKind.Chat, null, null, null, false, "chat-1", null, null, null, null, null, null, null, null, false, Text: "Confirm project axes & preview")),
            timeout.Token);
        var chat = AssistantIpc.Parse(await AssistantPipeFrames.ReadAsync(client, timeout.Token));
        Assert.Equal(AssistantIpcKind.ChatResult, chat.Message!.Kind);
        Assert.Empty(preview.Requests);
        Assert.Equal(MepDevicePlacementProposalStates.NeedsCalibration, store.Current!.State);

        await AssistantPipeFrames.WriteAsync(
            client,
            AssistantIpc.Serialize(new AssistantIpcMessage(
                AssistantIpcKind.ConfirmProjectAxesPreview, null, null, null, false, null, null, null, null, null, null, null, null, null, false)),
            timeout.Token);
        var previewMessage = AssistantIpc.Parse(await AssistantPipeFrames.ReadAsync(client, timeout.Token));
        Assert.Equal(AssistantIpcKind.PreviewResult, previewMessage.Message!.Kind);
        Assert.False(previewMessage.Message.IsError);
        Assert.Single(preview.Requests);
        Assert.DoesNotContain("revit_request_mep_device_placement_review", session.Tools);
        Assert.DoesNotContain("revit_apply_mep_device_placements", session.Tools);
        Assert.DoesNotContain(IMepDevicePlacementPreviewClient.ToolName, session.Tools);
        AssertHidden(previewMessage.Message.Body!);

        await AssistantPipeFrames.WriteAsync(
            client,
            AssistantIpc.Serialize(new AssistantIpcMessage(AssistantIpcKind.Shutdown, null, null, null, false, null, null, null, null, null, null, null, null, null, false)),
            timeout.Token);
        Assert.Equal(0, await host);
        Assert.Null(store.Preview);
    }

    private static (MepDevicePlacementPreviewOrchestrator Orchestrator, MepDevicePlacementProposalStore Store, SelectionSession Session, RecordingPreview Preview) Create(
        string unit,
        DateTimeOffset now)
    {
        return Create(unit, () => now);
    }

    private static (MepDevicePlacementPreviewOrchestrator Orchestrator, MepDevicePlacementProposalStore Store, SelectionSession Session, RecordingPreview Preview) Create(
        string unit,
        Func<DateTimeOffset> now)
    {
        var store = new MepDevicePlacementProposalStore();
        store.Replace(Bound(unit, null));
        var session = new SelectionSession(Selection(1, false, "[\"" + TemplateRef + "\"]"));
        var preview = new RecordingPreview();
        var orchestrator = new MepDevicePlacementPreviewOrchestrator(session, preview, store, InstanceId, now);
        return (orchestrator, store, session, preview);
    }

    private static MepDevicePlacementProposal Bound(string unit, string? source)
    {
        return MepDevicePlacementProposal.Create(
            unit,
            [
                new MepDevicePlacementProposalItem(1000, 0, 0, null, null),
                new MepDevicePlacementProposalItem(2000, 0, 0, null, null),
            ],
            MepDevicePlacementProposalReasons.CoordinateBasisUnestablished,
            InstanceId,
            DocumentId,
            TemplateRef,
            source);
    }

    private static string Selection(int count, bool truncated, string refs)
    {
        return "{\"context\":{\"document_id\":\"" + DocumentId + "\",\"instance_id\":\"wire-instance\"},\"selected_count\":"
            + count.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + ",\"truncated\":" + (truncated ? "true" : "false") + ",\"element_refs\":" + refs + "}";
    }

    private static PreviewMepDevicePlacementsResult NotReadyResult()
    {
        return new PreviewMepDevicePlacementsResult
        {
            Context = new PreviewMepDevicePlacementsContext { InstanceId = InstanceId, DocumentId = DocumentId },
            Ready = false,
            Template = new PreviewMepDeviceTemplate
            {
                ElementRef = TemplateRef,
                Status = PreviewMepDeviceTemplateStatus.UnsupportedCategory,
                CategoryName = "Electrical Fixtures",
            },
            Items =
            [
                Item(PreviewMepDevicePlacementItemStatus.UnsupportedPosition, 1000),
                Item(PreviewMepDevicePlacementItemStatus.Ok, 2000),
            ],
        };
    }

    private static PreviewMepDevicePlacementsResult Mismatch(string mismatch, DateTimeOffset expiresAt)
    {
        var ready = ReadyResult(expiresAt);
        return mismatch switch
        {
            "instance" => new PreviewMepDevicePlacementsResult
            {
                Context = new PreviewMepDevicePlacementsContext { InstanceId = "inst-other", DocumentId = DocumentId },
                Ready = ready.Ready,
                Template = ready.Template,
                Items = ready.Items,
                IntentRef = ready.IntentRef,
                IntentFingerprint = ready.IntentFingerprint,
                ExpiresAt = ready.ExpiresAt,
            },
            "document" => new PreviewMepDevicePlacementsResult
            {
                Context = new PreviewMepDevicePlacementsContext { InstanceId = InstanceId, DocumentId = "doc-other" },
                Ready = ready.Ready,
                Template = ready.Template,
                Items = ready.Items,
                IntentRef = ready.IntentRef,
                IntentFingerprint = ready.IntentFingerprint,
                ExpiresAt = ready.ExpiresAt,
            },
            "template" => new PreviewMepDevicePlacementsResult
            {
                Context = ready.Context,
                Ready = ready.Ready,
                Template = new PreviewMepDeviceTemplate
                {
                    ElementRef = "ref-other",
                    Status = ready.Template.Status,
                    PlacementMode = ready.Template.PlacementMode,
                    FamilyName = ready.Template.FamilyName,
                    TypeName = ready.Template.TypeName,
                    CategoryName = ready.Template.CategoryName,
                    LevelName = ready.Template.LevelName,
                },
                Items = ready.Items,
                IntentRef = ready.IntentRef,
                IntentFingerprint = ready.IntentFingerprint,
                ExpiresAt = ready.ExpiresAt,
            },
            "count" => new PreviewMepDevicePlacementsResult
            {
                Context = ready.Context,
                Ready = ready.Ready,
                Template = ready.Template,
                Items = [Item(PreviewMepDevicePlacementItemStatus.Ok, 1000)],
                IntentRef = ready.IntentRef,
                IntentFingerprint = ready.IntentFingerprint,
                ExpiresAt = ready.ExpiresAt,
            },
            _ => new PreviewMepDevicePlacementsResult
            {
                Context = ready.Context,
                Ready = ready.Ready,
                Template = ready.Template,
                Items =
                [
                    Item(PreviewMepDevicePlacementItemStatus.Ok, 2000),
                    Item(PreviewMepDevicePlacementItemStatus.Ok, 1000),
                ],
                IntentRef = ready.IntentRef,
                IntentFingerprint = ready.IntentFingerprint,
                ExpiresAt = ready.ExpiresAt,
            },
        };
    }

    private static PreviewMepDevicePlacementsResult MalformedReady(string defect, DateTimeOffset expiresAt)
    {
        var ready = ReadyResult(expiresAt);
        return defect switch
        {
            "template" => new PreviewMepDevicePlacementsResult
            {
                Context = ready.Context,
                Ready = true,
                Template = new PreviewMepDeviceTemplate
                {
                    ElementRef = TemplateRef,
                    Status = PreviewMepDeviceTemplateStatus.UnsupportedHost,
                    PlacementMode = ready.Template.PlacementMode,
                    FamilyName = ready.Template.FamilyName,
                },
                Items = ready.Items,
                IntentRef = ready.IntentRef,
                IntentFingerprint = ready.IntentFingerprint,
                ExpiresAt = ready.ExpiresAt,
            },
            "item" => new PreviewMepDevicePlacementsResult
            {
                Context = ready.Context,
                Ready = true,
                Template = ready.Template,
                Items =
                [
                    Item(PreviewMepDevicePlacementItemStatus.OutOfHostBounds, 1000),
                    Item(PreviewMepDevicePlacementItemStatus.Ok, 2000),
                ],
                IntentRef = ready.IntentRef,
                IntentFingerprint = ready.IntentFingerprint,
                ExpiresAt = ready.ExpiresAt,
            },
            "intent" => new PreviewMepDevicePlacementsResult
            {
                Context = ready.Context,
                Ready = true,
                Template = ready.Template,
                Items = ready.Items,
                IntentRef = " ",
                IntentFingerprint = ready.IntentFingerprint,
                ExpiresAt = ready.ExpiresAt,
            },
            "fingerprint" => new PreviewMepDevicePlacementsResult
            {
                Context = ready.Context,
                Ready = true,
                Template = ready.Template,
                Items = ready.Items,
                IntentRef = ready.IntentRef,
                IntentFingerprint = "",
                ExpiresAt = ready.ExpiresAt,
            },
            _ => new PreviewMepDevicePlacementsResult
            {
                Context = ready.Context,
                Ready = true,
                Template = ready.Template,
                Items = ready.Items,
                IntentRef = ready.IntentRef,
                IntentFingerprint = ready.IntentFingerprint,
                ExpiresAt = null,
            },
        };
    }

    private static PreviewMepDevicePlacementsResult ReadyResult(DateTimeOffset expiresAt, string intentRef = IntentRef)
    {
        return new PreviewMepDevicePlacementsResult
        {
            Context = new PreviewMepDevicePlacementsContext { InstanceId = InstanceId, DocumentId = DocumentId },
            Ready = true,
            Template = new PreviewMepDeviceTemplate
            {
                ElementRef = TemplateRef,
                Status = PreviewMepDeviceTemplateStatus.Ok,
                PlacementMode = PreviewMepDevicePlacementMode.LevelBased,
                FamilyName = "Duplex Receptacle",
                TypeName = "GFCI",
                CategoryName = "Electrical Fixtures",
                LevelName = "L2",
                HostElementRef = "host-secret-4",
            },
            Items =
            [
                Item(PreviewMepDevicePlacementItemStatus.Ok, 1000),
                Item(PreviewMepDevicePlacementItemStatus.Ok, 2000),
            ],
            IntentRef = intentRef,
            IntentFingerprint = Fingerprint,
            ExpiresAt = expiresAt,
        };
    }

    private static PreviewMepDevicePlacementItem Item(PreviewMepDevicePlacementItemStatus status, double x = 1000)
    {
        return new PreviewMepDevicePlacementItem
        {
            OffsetX = x,
            OffsetY = 0,
            VerticalOffset = 0,
            Status = status,
        };
    }

    private static void AssertHidden(string body)
    {
        Assert.DoesNotContain(InstanceId, body, StringComparison.Ordinal);
        Assert.DoesNotContain(DocumentId, body, StringComparison.Ordinal);
        Assert.DoesNotContain(TemplateRef, body, StringComparison.Ordinal);
        Assert.DoesNotContain(IntentRef, body, StringComparison.Ordinal);
        Assert.DoesNotContain(Fingerprint, body, StringComparison.Ordinal);
        Assert.DoesNotContain("host-secret-4", body, StringComparison.Ordinal);
        Assert.DoesNotContain("wire-instance", body, StringComparison.Ordinal);
        Assert.DoesNotContain("C:\\", body, StringComparison.Ordinal);
    }

    private static AssistantIpcMessage Hello()
    {
        return new AssistantIpcMessage(AssistantIpcKind.Hello, InstanceId, "2026", "26.5.0.55", true, null, null, null, null, null, null, null, null, null, false);
    }

    private sealed class SelectionSession(string selectionJson) : IAssistantToolSession
    {
        public List<string> Tools { get; } = [];

        public string SelectionJson { get; set; } = selectionJson;

        public Task<AssistantMcpReadiness> InitializeAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new AssistantMcpReadiness(true, 14, [], null));
        }

        public Task<AssistantToolCall> CallAsync(string toolName, IReadOnlyDictionary<string, object?> arguments, CancellationToken cancellationToken)
        {
            Tools.Add(toolName);
            if (!string.Equals(toolName, AssistantTools.GetSelection, StringComparison.Ordinal))
            {
                return Task.FromResult(new AssistantToolCall(true, "tool_not_allowed", "This Assistant workflow cannot call that tool.", null));
            }

            using var document = JsonDocument.Parse(SelectionJson);
            return Task.FromResult(new AssistantToolCall(false, null, null, document.RootElement.Clone()));
        }
    }

    private sealed class RecordingPreview : IMepDevicePlacementPreviewClient
    {
        public List<PreviewMepDevicePlacementsRequest> Requests { get; } = [];

        public string? InstanceId { get; private set; }

        public PreviewMepDevicePlacementsResult Result { get; set; } = null!;

        public Task<AssistantToolCall> PreviewAsync(
            PreviewMepDevicePlacementsRequest request,
            string instanceId,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            InstanceId = instanceId;
            var json = JsonSerializer.Serialize(Result, ContractJson.Options);
            using var document = JsonDocument.Parse(json);
            return Task.FromResult(new AssistantToolCall(false, null, null, document.RootElement.Clone()));
        }
    }

    private sealed class IdleRuntime : IAssistantAgentRuntime
    {
        public AssistantProviderStatus Status { get; } = new("GitHub Copilot", AssistantProviderState.Ready, "auto", null);

        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<AssistantAgentTurn> SendAsync(AssistantAgentRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new AssistantAgentTurn("BIM-IT Assistant", "The model cannot preview.", false, []));
        }

        public Task ResetConversationAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
