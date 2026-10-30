using System.Text.Json;
using BimIt.Revit.Assistant.Ipc;
using BimIt.Revit.Contracts;
using Xunit;

namespace BimIt.Revit.Assistant.Tests;

public sealed class AssistantReviewApplyOrchestrationTests
{
    private const string InstanceId = "inst-secret-1";
    private const string DocumentId = "doc-secret-9f3a";
    private const string TemplateRef = "ref-secret-77";
    private const string IntentRef = "intent-secret-abc";
    private const string Fingerprint = "fp-secret-xyz";
    private const string CreatedRef = "created-secret-1";

    [Fact]
    public void Local_review_and_apply_are_not_model_tools()
    {
        Assert.Equal(3, AssistantModelTools.LocalNames.Length);
        Assert.Equal(9, AssistantTools.AllowedReadTools.Length);
        Assert.Equal(3, AssistantTools.WorkflowTools.Length);
        Assert.False(AssistantIpc.GrantsApprovalAuthority);
        Assert.False(AssistantIpc.IsModelToolName("reviewInRevit"));
        Assert.False(AssistantIpc.IsModelToolName("applyApprovedPlacements"));
        Assert.False(AssistantIpc.IsModelToolName(IMepDevicePlacementReviewClient.ToolName));
        Assert.False(AssistantIpc.IsModelToolName(IMepDevicePlacementApplyClient.ToolName));
        Assert.DoesNotContain(IMepDevicePlacementReviewClient.ToolName, AssistantTools.AllowedReadTools);
        Assert.DoesNotContain(IMepDevicePlacementApplyClient.ToolName, AssistantTools.AllowedReadTools);
        Assert.True(AssistantTools.IsTrustedWriteTool(IMepDevicePlacementReviewClient.ToolName));
        Assert.True(AssistantTools.IsTrustedWriteTool(IMepDevicePlacementApplyClient.ToolName));
        Assert.Equal(AssistantIpcKind.ReviewInRevit, AssistantIpc.Parse("""{"type":"reviewInRevit"}""").Message!.Kind);
        Assert.Equal(AssistantIpcKind.ApplyApprovedPlacements, AssistantIpc.Parse("""{"type":"applyApprovedPlacements"}""").Message!.Kind);
        Assert.False(AssistantIpc.Parse("""{"type":"reviewInRevit","text":"approve"}""").Accepted);
        Assert.False(AssistantIpc.Parse("""{"type":"applyApprovedPlacements","intent_ref":"x"}""").Accepted);
    }

    [Theory]
    [InlineData(RequestMepDevicePlacementReviewStatus.Started, false, "Revit review opened.")]
    [InlineData(RequestMepDevicePlacementReviewStatus.AlreadyActive, false, "The same review is already open.")]
    [InlineData(RequestMepDevicePlacementReviewStatus.Busy, false, "Another review is active.")]
    [InlineData(RequestMepDevicePlacementReviewStatus.Terminal, false, "This intent already has a local terminal decision.")]
    [InlineData(RequestMepDevicePlacementReviewStatus.Unavailable, true, "The Revit review could not be opened.")]
    public async Task Review_status_is_mapped_from_the_hidden_preview_intent(
        RequestMepDevicePlacementReviewStatus status,
        bool isError,
        string body)
    {
        var now = new DateTimeOffset(2026, 10, 19, 18, 0, 0, TimeSpan.Zero);
        var (orchestrator, _, review, apply, session) = Create(now);
        review.Status = status;

        var outcome = await orchestrator.ReviewAsync(CancellationToken.None);

        Assert.Equal(isError, outcome.IsError);
        Assert.Equal(body, outcome.Body);
        var call = Assert.Single(review.Calls);
        Assert.Equal(InstanceId, call.InstanceId);
        Assert.Equal(IntentRef, call.IntentRef);
        Assert.Empty(apply.Calls);
        Assert.Empty(session.Tools);
        AssertHidden(outcome.Body);
    }

    [Fact]
    public async Task Expired_or_missing_preview_cannot_review_or_apply()
    {
        var now = new DateTimeOffset(2026, 10, 19, 18, 0, 0, TimeSpan.Zero);
        var (missingReview, _, review, apply, _) = Create(now, rememberPreview: false);
        var missing = await missingReview.ReviewAsync(CancellationToken.None);
        Assert.Equal("There is no placement preview to review.", missing.Body);
        Assert.Empty(review.Calls);

        var (expiredOrchestrator, _, expiredReview, expiredApply, _) = Create(now, expiresAt: now);
        var expiredReviewOutcome = await expiredOrchestrator.ReviewAsync(CancellationToken.None);
        var expiredApplyOutcome = await expiredOrchestrator.ApplyAsync(CancellationToken.None);
        Assert.Equal("The placement preview has expired. Confirm project axes and preview again.", expiredReviewOutcome.Body);
        Assert.Equal("The placement preview has expired. Confirm project axes and preview again.", expiredApplyOutcome.Body);
        Assert.Empty(expiredReview.Calls);
        Assert.Empty(expiredApply.Calls);
    }

    [Fact]
    public async Task Apply_before_approval_is_not_terminal()
    {
        var now = new DateTimeOffset(2026, 10, 19, 18, 0, 0, TimeSpan.Zero);
        var (orchestrator, store, review, apply, session) = Create(now);
        apply.Result = new ApplyMepDevicePlacementsResult { Status = ApplyMepDevicePlacementsStatus.ApprovalRequired };

        var outcome = await orchestrator.ApplyAsync(CancellationToken.None);

        Assert.False(outcome.IsError);
        Assert.Equal("Revit-local approval is still required. No placements were created.", outcome.Body);
        Assert.Null(store.Apply);
        Assert.Empty(review.Calls);
        Assert.Empty(session.Tools);
        Assert.Equal(IntentRef, Assert.Single(apply.Calls).IntentRef);
        AssertHidden(outcome.Body);
    }

    [Fact]
    public async Task Applied_result_hides_refs_and_a_repeat_does_not_call_apply_again()
    {
        var now = new DateTimeOffset(2026, 10, 19, 18, 0, 0, TimeSpan.Zero);
        var (orchestrator, store, _, apply, _) = Create(now);
        apply.Result = AppliedResult();

        var first = await orchestrator.ApplyAsync(CancellationToken.None);
        var second = await orchestrator.ApplyAsync(CancellationToken.None);

        Assert.Equal("Applied 2 placements. Revit verification passed.", first.Body);
        Assert.Equal(first.Body, second.Body);
        Assert.False(first.IsError);
        Assert.Single(apply.Calls);
        Assert.NotNull(store.Apply);
        AssertHidden(first.Body);
        Assert.DoesNotContain(CreatedRef, first.Body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 1)]
    public async Task Malformed_applied_result_fails_closed(int createdCount, int refCount)
    {
        var now = new DateTimeOffset(2026, 10, 19, 18, 0, 0, TimeSpan.Zero);
        var (orchestrator, store, _, apply, _) = Create(now);
        apply.Result = new ApplyMepDevicePlacementsResult
        {
            Status = ApplyMepDevicePlacementsStatus.Applied,
            CreatedCount = createdCount,
            CreatedElementRefs = Enumerable.Range(0, refCount).Select(index => index == 0 ? " " : CreatedRef + index).ToArray(),
        };

        var outcome = await orchestrator.ApplyAsync(CancellationToken.None);

        Assert.True(outcome.IsError);
        Assert.Equal("The placement apply could not be completed.", outcome.Body);
        Assert.Null(store.Apply);
        Assert.DoesNotContain(CreatedRef, outcome.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Committed_unverified_is_terminal_and_is_not_retried()
    {
        var now = new DateTimeOffset(2026, 10, 19, 18, 0, 0, TimeSpan.Zero);
        var (orchestrator, store, review, apply, _) = Create(now);
        apply.Result = new ApplyMepDevicePlacementsResult { Status = ApplyMepDevicePlacementsStatus.CommittedUnverified };

        var first = await orchestrator.ApplyAsync(CancellationToken.None);
        var second = await orchestrator.ApplyAsync(CancellationToken.None);

        Assert.True(first.IsError);
        Assert.Equal("A transaction committed, but Revit verification failed. Do not retry this placement.", first.Body);
        Assert.Equal(first.Body, second.Body);
        Assert.Single(apply.Calls);
        Assert.Empty(review.Calls);
        Assert.NotNull(store.Apply);
        Assert.DoesNotContain("try again", first.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task In_progress_is_not_cached()
    {
        var now = new DateTimeOffset(2026, 10, 19, 18, 0, 0, TimeSpan.Zero);
        var (orchestrator, store, _, apply, _) = Create(now);
        apply.Result = new ApplyMepDevicePlacementsResult { Status = ApplyMepDevicePlacementsStatus.InProgress };

        await orchestrator.ApplyAsync(CancellationToken.None);
        await orchestrator.ApplyAsync(CancellationToken.None);

        Assert.Equal(2, apply.Calls.Count);
        Assert.Null(store.Apply);
    }

    [Fact]
    public void New_proposal_attachment_invalidation_and_clear_drop_apply_state()
    {
        var store = ReadyStore(DateTimeOffset.UtcNow.AddMinutes(5));
        store.RememberTerminalApply(IntentRef, "Applied 2 placements. Revit verification passed.", false);
        Assert.NotNull(store.Apply);

        store.Replace(Bound("artifact-hidden"));
        Assert.Null(store.Apply);
        Assert.Null(store.Preview);

        store.RememberPreview(IntentRef, Fingerprint, DateTimeOffset.UtcNow.AddMinutes(5), "Placements: 2");
        store.RememberTerminalApply(IntentRef, "Applied 2 placements. Revit verification passed.", false);
        store.NoteAttachmentIdentityChanged("artifact-hidden", "artifact-next");
        Assert.Null(store.Current);
        Assert.Null(store.Apply);

        store.Replace(Bound(null));
        store.RememberPreview(IntentRef, Fingerprint, DateTimeOffset.UtcNow.AddMinutes(5), "Placements: 2");
        store.RememberTerminalApply(IntentRef, "Applied 2 placements. Revit verification passed.", false);
        store.Clear();
        Assert.Null(store.Apply);

        store.Replace(Bound(null));
        store.RememberPreview(IntentRef, Fingerprint, DateTimeOffset.UtcNow.AddMinutes(5), "Placements: 2");
        store.RememberTerminalApply(IntentRef, "Applied 2 placements. Revit verification passed.", false);
        Assert.False(store.RememberPreview("intent-next", Fingerprint, DateTimeOffset.UtcNow.AddMinutes(5), "Placements: 2"));
        Assert.Equal(IntentRef, store.Preview!.IntentRef);
        Assert.NotNull(store.Apply);
    }

    [Fact]
    public async Task Applied_preview_expiry_does_not_create_another_preview()
    {
        var now = new DateTimeOffset(2026, 10, 19, 18, 0, 0, TimeSpan.Zero);
        var (previewOrchestrator, store, preview, review, apply) = TerminalGeneration(now, "Applied 2 placements. Revit verification passed.", false);

        var confirm = await previewOrchestrator.ConfirmAsync(CancellationToken.None);
        var reviewOutcome = await new MepDevicePlacementReviewApplyOrchestrator(review, apply, store, InstanceId, () => now.AddMinutes(10)).ReviewAsync(CancellationToken.None);
        var secondApply = await new MepDevicePlacementReviewApplyOrchestrator(review, apply, store, InstanceId, () => now.AddMinutes(10)).ApplyAsync(CancellationToken.None);

        Assert.True(confirm.IsError);
        Assert.Equal(MepDevicePlacementPreviewOrchestrator.TerminalGenerationMessage, confirm.Body);
        Assert.Empty(preview.Calls);
        Assert.Equal(MepDevicePlacementReviewApplyOrchestrator.TerminalGenerationMessage, reviewOutcome.Body);
        Assert.Empty(review.Calls);
        Assert.Equal("Applied 2 placements. Revit verification passed.", secondApply.Body);
        Assert.Empty(apply.Calls);
        Assert.Equal(IntentRef, store.Apply!.IntentRef);
        Assert.Equal(IntentRef, store.Preview!.IntentRef);
    }

    [Fact]
    public async Task Committed_unverified_preview_expiry_does_not_create_another_preview()
    {
        var now = new DateTimeOffset(2026, 10, 19, 18, 0, 0, TimeSpan.Zero);
        var (previewOrchestrator, store, preview, _, _) = TerminalGeneration(
            now,
            "A transaction committed, but Revit verification failed. Do not retry this placement.",
            true);

        var confirm = await previewOrchestrator.ConfirmAsync(CancellationToken.None);

        Assert.True(confirm.IsError);
        Assert.Equal(MepDevicePlacementPreviewOrchestrator.TerminalGenerationMessage, confirm.Body);
        Assert.Empty(preview.Calls);
        Assert.True(store.Apply!.IsError);
        Assert.Equal(IntentRef, store.Preview!.IntentRef);
    }

    [Fact]
    public async Task Replacing_the_proposal_clears_the_terminal_apply_and_allows_a_new_preview()
    {
        var now = new DateTimeOffset(2026, 10, 19, 18, 0, 0, TimeSpan.Zero);
        var (previewOrchestrator, store, preview, _, _) = TerminalGeneration(now, "Applied 2 placements. Revit verification passed.", false);
        store.Replace(Bound("artifact-hidden"));
        preview.Result = ReadyPreview(now.AddMinutes(30), "intent-next");

        var confirm = await previewOrchestrator.ConfirmAsync(CancellationToken.None);

        Assert.False(confirm.IsError);
        Assert.Contains("Preview ready: yes", confirm.Body, StringComparison.Ordinal);
        Assert.Single(preview.Calls);
        Assert.Null(store.Apply);
        Assert.Equal("intent-next", store.Preview!.IntentRef);
    }

    [Theory]
    [InlineData(ApplyMepDevicePlacementsStatus.Stale, "This placement preview is stale. No automatic retry will be attempted.")]
    [InlineData(ApplyMepDevicePlacementsStatus.TransactionFailed, "The placement transaction failed. No automatic retry will be attempted.")]
    [InlineData(ApplyMepDevicePlacementsStatus.Indeterminate, "The placement result is indeterminate. No automatic retry will be attempted.")]
    [InlineData(ApplyMepDevicePlacementsStatus.AuditFailed, "The placement audit failed. No automatic retry will be attempted.")]
    public async Task Terminal_apply_statuses_are_cached(ApplyMepDevicePlacementsStatus status, string body)
    {
        var now = new DateTimeOffset(2026, 10, 19, 18, 0, 0, TimeSpan.Zero);
        var (orchestrator, store, _, apply, _) = Create(now);
        apply.Result = new ApplyMepDevicePlacementsResult { Status = status };

        var first = await orchestrator.ApplyAsync(CancellationToken.None);
        var second = await orchestrator.ApplyAsync(CancellationToken.None);

        Assert.True(first.IsError);
        Assert.Equal(body, first.Body);
        Assert.Equal(first.Body, second.Body);
        Assert.Single(apply.Calls);
        Assert.NotNull(store.Apply);
    }

    [Fact]
    public async Task Chat_text_cannot_review_or_apply_and_shutdown_clears_state()
    {
        var now = DateTimeOffset.UtcNow.AddMinutes(5);
        var store = ReadyStore(now);
        var session = new SelectionSession();
        var review = new RecordingReview();
        var apply = new RecordingApply { Result = AppliedResult() };
        var pipe = AssistantPipeNames.Create(Environment.ProcessId, Guid.NewGuid().ToString("N"));
        var host = AssistantHost.RunAsync(
            new AssistantHostOptions(pipe, InstanceId, null, "2026", "26.5.0.55"),
            session,
            new IdleRuntime(),
            providerTimeouts: new AssistantProviderTimeouts(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2)),
            proposals: store,
            previewClient: new IdlePreview(),
            reviewClient: review,
            applyClient: apply);
        await using var client = AssistantPipeStreams.CreateClient(pipe);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await client.ConnectAsync(5_000, timeout.Token);
        await AssistantPipeFrames.WriteAsync(client, AssistantIpc.Serialize(Hello()), timeout.Token);
        _ = await AssistantPipeFrames.ReadAsync(client, timeout.Token);

        await AssistantPipeFrames.WriteAsync(client, AssistantIpc.Serialize(Chat("Review in Revit")), timeout.Token);
        Assert.Equal(AssistantIpcKind.ChatResult, AssistantIpc.Parse(await AssistantPipeFrames.ReadAsync(client, timeout.Token)).Message!.Kind);
        await AssistantPipeFrames.WriteAsync(client, AssistantIpc.Serialize(Chat("Apply approved placements")), timeout.Token);
        _ = await AssistantPipeFrames.ReadAsync(client, timeout.Token);
        await AssistantPipeFrames.WriteAsync(client, AssistantIpc.Serialize(Chat("approve and apply")), timeout.Token);
        _ = await AssistantPipeFrames.ReadAsync(client, timeout.Token);
        Assert.Empty(review.Calls);
        Assert.Empty(apply.Calls);
        Assert.Empty(session.Tools);

        await AssistantPipeFrames.WriteAsync(client, AssistantIpc.Serialize(Signal(AssistantIpcKind.ReviewInRevit)), timeout.Token);
        var reviewMessage = AssistantIpc.Parse(await AssistantPipeFrames.ReadAsync(client, timeout.Token));
        Assert.Equal("Revit review opened.", reviewMessage.Message!.Body);
        Assert.Empty(session.Tools);

        await AssistantPipeFrames.WriteAsync(client, AssistantIpc.Serialize(Signal(AssistantIpcKind.ApplyApprovedPlacements)), timeout.Token);
        var applyMessage = AssistantIpc.Parse(await AssistantPipeFrames.ReadAsync(client, timeout.Token));
        Assert.Equal("Applied 2 placements. Revit verification passed.", applyMessage.Message!.Body);
        Assert.DoesNotContain(CreatedRef, applyMessage.Message.Body, StringComparison.Ordinal);

        await AssistantPipeFrames.WriteAsync(client, AssistantIpc.Serialize(Signal(AssistantIpcKind.ResetConversation)), timeout.Token);
        _ = await AssistantPipeFrames.ReadAsync(client, timeout.Token);
        await AssistantPipeFrames.WriteAsync(client, AssistantIpc.Serialize(Signal(AssistantIpcKind.ApplyApprovedPlacements)), timeout.Token);
        var afterReset = AssistantIpc.Parse(await AssistantPipeFrames.ReadAsync(client, timeout.Token));
        Assert.Equal("There is no placement preview to apply.", afterReset.Message!.Body);
        Assert.Single(apply.Calls);

        await AssistantPipeFrames.WriteAsync(client, AssistantIpc.Serialize(Signal(AssistantIpcKind.Shutdown)), timeout.Token);
        Assert.Equal(0, await host);
        Assert.Null(store.Apply);
        Assert.Null(store.Preview);
    }

    private static (MepDevicePlacementReviewApplyOrchestrator Orchestrator, MepDevicePlacementProposalStore Store, RecordingReview Review, RecordingApply Apply, SelectionSession Session) Create(
        DateTimeOffset now,
        bool rememberPreview = true,
        DateTimeOffset? expiresAt = null)
    {
        var store = ReadyStore(rememberPreview ? expiresAt ?? now.AddMinutes(5) : null);
        var review = new RecordingReview();
        var apply = new RecordingApply();
        var session = new SelectionSession();
        var orchestrator = new MepDevicePlacementReviewApplyOrchestrator(review, apply, store, InstanceId, () => now);
        return (orchestrator, store, review, apply, session);
    }

    private static (MepDevicePlacementPreviewOrchestrator Preview, MepDevicePlacementProposalStore Store, CountingPreview PreviewClient, RecordingReview Review, RecordingApply Apply) TerminalGeneration(
        DateTimeOffset now,
        string summary,
        bool isError)
    {
        var store = ReadyStore(now);
        store.RememberTerminalApply(IntentRef, summary, isError);
        var preview = new CountingPreview();
        var review = new RecordingReview();
        var apply = new RecordingApply();
        var orchestrator = new MepDevicePlacementPreviewOrchestrator(
            new MatchingSelection(),
            preview,
            store,
            InstanceId,
            () => now.AddMinutes(10));
        return (orchestrator, store, preview, review, apply);
    }

    private static PreviewMepDevicePlacementsResult ReadyPreview(DateTimeOffset expiresAt, string intentRef)
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
                FamilyName = "HeatRecoveryUnit",
                TypeName = "Heat Recovery Unit (HRU)",
                CategoryName = "Mechanical Equipment",
                LevelName = "L2",
            },
            Items =
            [
                new PreviewMepDevicePlacementItem { OffsetX = 1000, OffsetY = 0, VerticalOffset = 0, Status = PreviewMepDevicePlacementItemStatus.Ok },
                new PreviewMepDevicePlacementItem { OffsetX = 2000, OffsetY = 0, VerticalOffset = 0, Status = PreviewMepDevicePlacementItemStatus.Ok },
            ],
            IntentRef = intentRef,
            IntentFingerprint = Fingerprint,
            ExpiresAt = expiresAt,
        };
    }

    private static MepDevicePlacementProposalStore ReadyStore(DateTimeOffset? expiresAt)
    {
        var store = new MepDevicePlacementProposalStore();
        store.Replace(Bound("artifact-hidden"));
        if (expiresAt is DateTimeOffset expiry)
        {
            store.RememberPreview(IntentRef, Fingerprint, expiry, "Placements: 2");
        }

        return store;
    }

    private static MepDevicePlacementProposal Bound(string? source)
    {
        return MepDevicePlacementProposal.Create(
            "mm",
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

    private static ApplyMepDevicePlacementsResult AppliedResult()
    {
        return new ApplyMepDevicePlacementsResult
        {
            Status = ApplyMepDevicePlacementsStatus.Applied,
            CreatedCount = 2,
            CreatedElementRefs = [CreatedRef, CreatedRef + "-b"],
        };
    }

    private static void AssertHidden(string body)
    {
        Assert.DoesNotContain(InstanceId, body, StringComparison.Ordinal);
        Assert.DoesNotContain(DocumentId, body, StringComparison.Ordinal);
        Assert.DoesNotContain(TemplateRef, body, StringComparison.Ordinal);
        Assert.DoesNotContain(IntentRef, body, StringComparison.Ordinal);
        Assert.DoesNotContain(Fingerprint, body, StringComparison.Ordinal);
    }

    private static AssistantIpcMessage Hello()
    {
        return new AssistantIpcMessage(AssistantIpcKind.Hello, InstanceId, "2026", "26.5.0.55", true, null, null, null, null, null, null, null, null, null, false);
    }

    private static AssistantIpcMessage Chat(string text)
    {
        return new AssistantIpcMessage(
            AssistantIpcKind.Chat, null, null, null, false, "chat-1", null, null, null, null, null, null, null, null, false, Text: text);
    }

    private static AssistantIpcMessage Signal(AssistantIpcKind kind)
    {
        return new AssistantIpcMessage(kind, null, null, null, false, null, null, null, null, null, null, null, null, null, false);
    }

    private sealed class MatchingSelection : IAssistantToolSession
    {
        public Task<AssistantMcpReadiness> InitializeAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new AssistantMcpReadiness(true, 14, [], null));
        }

        public Task<AssistantToolCall> CallAsync(string toolName, IReadOnlyDictionary<string, object?> arguments, CancellationToken cancellationToken)
        {
            if (!string.Equals(toolName, AssistantTools.GetSelection, StringComparison.Ordinal))
            {
                return Task.FromResult(new AssistantToolCall(true, "tool_not_allowed", "This Assistant workflow cannot call that tool.", null));
            }

            var json = "{\"context\":{\"document_id\":\"" + DocumentId + "\"},\"selected_count\":1,\"truncated\":false,\"element_refs\":[\"" + TemplateRef + "\"]}";
            using var document = JsonDocument.Parse(json);
            return Task.FromResult(new AssistantToolCall(false, null, null, document.RootElement.Clone()));
        }
    }

    private sealed class CountingPreview : IMepDevicePlacementPreviewClient
    {
        public List<string> Calls { get; } = [];

        public PreviewMepDevicePlacementsResult Result { get; set; } = null!;

        public Task<AssistantToolCall> PreviewAsync(PreviewMepDevicePlacementsRequest request, string instanceId, CancellationToken cancellationToken)
        {
            Calls.Add(instanceId);
            var json = JsonSerializer.Serialize(Result, ContractJson.Options);
            using var document = JsonDocument.Parse(json);
            return Task.FromResult(new AssistantToolCall(false, null, null, document.RootElement.Clone()));
        }
    }

    private sealed class SelectionSession : IAssistantToolSession
    {
        public List<string> Tools { get; } = [];

        public Task<AssistantMcpReadiness> InitializeAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new AssistantMcpReadiness(true, 14, [], null));
        }

        public Task<AssistantToolCall> CallAsync(string toolName, IReadOnlyDictionary<string, object?> arguments, CancellationToken cancellationToken)
        {
            Tools.Add(toolName);
            return Task.FromResult(new AssistantToolCall(true, "tool_not_allowed", "This Assistant workflow cannot call that tool.", null));
        }
    }

    private sealed class RecordingReview : IMepDevicePlacementReviewClient
    {
        public List<(string InstanceId, string IntentRef)> Calls { get; } = [];

        public RequestMepDevicePlacementReviewStatus Status { get; set; } = RequestMepDevicePlacementReviewStatus.Started;

        public Task<AssistantToolCall> ReviewAsync(string instanceId, string intentRef, CancellationToken cancellationToken)
        {
            Calls.Add((instanceId, intentRef));
            return Task.FromResult(Structured(new RequestMepDevicePlacementReviewResult { Status = Status }));
        }
    }

    private sealed class RecordingApply : IMepDevicePlacementApplyClient
    {
        public List<(string InstanceId, string IntentRef)> Calls { get; } = [];

        public ApplyMepDevicePlacementsResult Result { get; set; } = new() { Status = ApplyMepDevicePlacementsStatus.ApprovalRequired };

        public Task<AssistantToolCall> ApplyAsync(string instanceId, string intentRef, CancellationToken cancellationToken)
        {
            Calls.Add((instanceId, intentRef));
            return Task.FromResult(Structured(Result));
        }
    }

    private sealed class IdlePreview : IMepDevicePlacementPreviewClient
    {
        public Task<AssistantToolCall> PreviewAsync(PreviewMepDevicePlacementsRequest request, string instanceId, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Preview must not be called.");
        }
    }

    private sealed class IdleRuntime : IAssistantAgentRuntime
    {
        public AssistantProviderStatus Status { get; } = new("GitHub Copilot", AssistantProviderState.Ready, "auto", null);

        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<AssistantAgentTurn> SendAsync(AssistantAgentRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new AssistantAgentTurn("BIM-IT Assistant", "The model cannot review or apply.", false, []));
        }

        public Task ResetConversationAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static AssistantToolCall Structured<T>(T value)
    {
        var json = JsonSerializer.Serialize(value, ContractJson.Options);
        using var document = JsonDocument.Parse(json);
        return new AssistantToolCall(false, null, null, document.RootElement.Clone());
    }
}
