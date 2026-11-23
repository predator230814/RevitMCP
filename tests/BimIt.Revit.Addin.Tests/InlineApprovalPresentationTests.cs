using System.Text.Json;
using BimIt.Revit.Addin.Approval;
using BimIt.Revit.Addin.Assistant;
using BimIt.Revit.Addin.Intents;
using BimIt.Revit.Assistant.Ipc;
using BimIt.Revit.Contracts;
using Xunit;

namespace BimIt.Revit.Addin.Tests;

public sealed class InlineApprovalPresentationTests
{
    [Fact]
    public async Task Parameter_review_appears_inline_from_the_render_model()
    {
        var harness = Harness(ParameterIntent());
        var presented = await harness.Adapter.PresentReviewAsync(harness.IntentRef, CancellationToken.None);

        Assert.Equal(ApprovalReviewStatus.Started, presented.Status);
        Assert.Equal(0, harness.Processes.Starts);
        Assert.Equal(0, harness.Gate.FallbackShows);
        using var snapshot = JsonDocument.Parse(harness.Shell.SerializeSnapshot());
        var approval = snapshot.RootElement.GetProperty("approval");
        Assert.Equal("renderReview", approval.GetProperty("type").GetString());
        Assert.Equal(presented.SessionRef, approval.GetProperty("sessionRef").GetString());
        Assert.Equal("Comments", approval.GetProperty("items")[0].GetProperty("parameterName").GetString());
        Assert.Equal("after", approval.GetProperty("items")[0].GetProperty("proposed").GetProperty("value").GetString());
        Assert.Empty(snapshot.RootElement.GetProperty("messages").EnumerateArray());
        Assert.False(AssistantIpc.GrantsApprovalAuthority);
    }

    [Fact]
    public async Task Mep_review_appears_inline_from_the_render_model()
    {
        var harness = Harness(MepIntent());
        var presented = await harness.Adapter.PresentReviewAsync(
            harness.IntentRef,
            ControlledWriteIntentKind.MepDevicePlacements,
            CancellationToken.None);

        using var snapshot = JsonDocument.Parse(harness.Shell.SerializeSnapshot());
        var approval = snapshot.RootElement.GetProperty("approval");
        Assert.Equal("mep_device_placements", approval.GetProperty("reviewKind").GetString());
        Assert.Equal(presented.SessionRef, approval.GetProperty("sessionRef").GetString());
        Assert.Equal("L4", approval.GetProperty("levelName").GetString());
        Assert.Contains("X +1200 mm", approval.GetProperty("placementLines")[0].GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain("template-a", harness.Shell.SerializeSnapshot(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Approve_records_the_provider_decision_and_does_not_apply()
    {
        var harness = Harness(ParameterIntent());
        var presented = await harness.Adapter.PresentReviewAsync(harness.IntentRef, CancellationToken.None);

        harness.Shell.ReceivePageMessage("{\"type\":\"approveCurrent\",\"sessionRef\":\"" + presented.SessionRef + "\"}");

        Assert.Equal(ApprovalReviewStatus.Terminal, harness.Controller.BeginReview(harness.IntentRef, "doc-a").Status);
        Assert.Null(harness.Ui.Current);
        Assert.False(JsonDocument.Parse(harness.Shell.SerializeSnapshot()).RootElement.TryGetProperty("approval", out _));
        var consumed = harness.Provider.TryConsumeApproved(harness.IntentRef);
        Assert.Equal(ApprovalConsumeStatus.Consumed, consumed.Status);
        Assert.Equal(ApprovalConsumeStatus.NotApproved, harness.Provider.TryConsumeApproved(harness.IntentRef).Status);
    }

    [Fact]
    public async Task Reject_records_the_provider_decision()
    {
        var harness = Harness(ParameterIntent());
        var presented = await harness.Adapter.PresentReviewAsync(harness.IntentRef, CancellationToken.None);

        harness.Shell.ReceivePageMessage("{\"type\":\"rejectCurrent\",\"sessionRef\":\"" + presented.SessionRef + "\"}");

        Assert.Equal(ApprovalReviewStatus.Terminal, harness.Controller.BeginReview(harness.IntentRef, "doc-a").Status);
        Assert.Equal(ApprovalConsumeStatus.NotApproved, harness.Provider.TryConsumeApproved(harness.IntentRef).Status);
        Assert.False(JsonDocument.Parse(harness.Shell.SerializeSnapshot()).RootElement.TryGetProperty("approval", out _));
    }

    [Fact]
    public async Task Foreign_and_malformed_page_messages_do_not_approve()
    {
        var harness = Harness(ParameterIntent());
        var presented = await harness.Adapter.PresentReviewAsync(harness.IntentRef, CancellationToken.None);

        harness.Shell.ReceivePageMessage("{\"type\":\"approveCurrent\",\"sessionRef\":\"other-session\"}");
        harness.Shell.ReceivePageMessage("{\"type\":\"approveCurrent\"}");
        harness.Shell.ReceivePageMessage("{\"type\":\"approveCurrent\",\"sessionRef\":\"" + presented.SessionRef + "\",\"intentRef\":\"forged\"}");
        harness.Shell.ReceivePageMessage("{");

        Assert.Equal(ApprovalConsumeStatus.NotApproved, harness.Provider.TryConsumeApproved(harness.IntentRef).Status);
        Assert.Equal(presented.SessionRef, harness.Ui.Current!.SessionRef);
        Assert.False(AssistantIpc.Parse("{\"type\":\"approveCurrent\",\"sessionRef\":\"" + presented.SessionRef + "\"}").Accepted);
        Assert.False(AssistantIpc.IsModelToolName("revit_apply_parameter_updates"));
        Assert.False(AssistantIpc.IsModelToolName("revit_apply_mep_device_placements"));
    }

    [Fact]
    public async Task Assistant_show_failure_uses_the_same_provider_session_on_the_fallback_pane()
    {
        var harness = Harness(ParameterIntent());
        harness.Gate.PrimarySucceeds = false;
        var fallback = new RecordingSurface();
        harness.Ui.RegisterSurface(fallback);

        var presented = await harness.Adapter.PresentReviewAsync(harness.IntentRef, CancellationToken.None);

        Assert.Equal(ApprovalReviewStatus.Started, presented.Status);
        Assert.Equal(1, harness.Gate.PrimaryShows);
        Assert.Equal(1, harness.Gate.FallbackShows);
        Assert.False(JsonDocument.Parse(harness.Shell.SerializeSnapshot()).RootElement.TryGetProperty("approval", out _));
        Assert.Contains(presented.SessionRef!, fallback.Last, StringComparison.Ordinal);
        Assert.Equal(presented.SessionRef, harness.Ui.Current!.SessionRef);
        Assert.Equal(ApprovalReviewStatus.AlreadyActive, harness.Controller.BeginReview(harness.IntentRef, "doc-a").Status);
    }

    [Fact]
    public async Task Neither_surface_returns_unavailable_and_records_no_decision()
    {
        var harness = Harness(ParameterIntent());
        harness.Gate.PrimarySucceeds = false;
        harness.Gate.ShowSucceeds = false;

        var presented = await harness.Adapter.PresentReviewAsync(harness.IntentRef, CancellationToken.None);

        Assert.Equal(ApprovalReviewStatus.Unavailable, presented.Status);
        Assert.Null(harness.Ui.Current);
        Assert.Equal(ApprovalConsumeStatus.NotApproved, harness.Provider.TryConsumeApproved(harness.IntentRef).Status);
    }

    [Fact]
    public async Task Dismiss_clears_the_inline_card_without_a_decision()
    {
        var harness = Harness(ParameterIntent());
        var presented = await harness.Adapter.PresentReviewAsync(harness.IntentRef, CancellationToken.None);

        harness.Shell.ReceivePageMessage("{\"type\":\"dismissCurrent\",\"sessionRef\":\"" + presented.SessionRef + "\"}");

        Assert.Null(harness.Ui.Current);
        Assert.False(JsonDocument.Parse(harness.Shell.SerializeSnapshot()).RootElement.TryGetProperty("approval", out _));
        Assert.Equal(ApprovalReviewStatus.Started, harness.Controller.BeginReview(harness.IntentRef, "doc-a").Status);
    }

    [Fact]
    public void Show_for_approval_does_not_start_the_assistant_process()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "src", "BimIt.Revit.Addin", "Assistant", "AssistantShell.cs"));
        var start = source.IndexOf("public bool ShowForApproval", StringComparison.Ordinal);
        var end = source.IndexOf("void IApprovalPresentationTarget.Publish", start, StringComparison.Ordinal);
        var method = source.Substring(start, end - start);
        Assert.DoesNotContain("OpenSessionAsync", method, StringComparison.Ordinal);
        Assert.DoesNotContain("EnsureAssistantAsync", method, StringComparison.Ordinal);
        Assert.DoesNotContain("GitHubCopilot", method, StringComparison.Ordinal);

        var page = File.ReadAllText(Path.Combine(RepoRoot(), "src", "BimIt.Revit.Addin", "Assistant", "Ui", "app.js"));
        var html = File.ReadAllText(Path.Combine(RepoRoot(), "src", "BimIt.Revit.Addin", "Assistant", "Ui", "index.html"));
        Assert.Contains("Trusted Revit preview", page, StringComparison.Ordinal);
        Assert.Contains("type: \"approveCurrent\"", page, StringComparison.Ordinal);
        Assert.Contains("type: \"rejectCurrent\"", page, StringComparison.Ordinal);
        Assert.Contains("type: \"dismissCurrent\"", page, StringComparison.Ordinal);
        Assert.Contains("default-src 'none'", html, StringComparison.Ordinal);
        Assert.Contains("id=\"approval\"", html, StringComparison.Ordinal);
    }

    private static HarnessState Harness(Func<EphemeralWriteIntentStore, IntentCreateResult> create)
    {
        var clock = new ManualTimeProvider();
        var store = new EphemeralWriteIntentStore(clock, () => new byte[32]);
        var created = create(store);
        Assert.Equal(IntentStoreCreateStatus.Created, created.Status);
        var provider = new RevitLocalApprovalProviderStateMachine(store, clock, () => "session");
        var controller = new RevitLocalApprovalInteractionController(store, provider);
        var ui = new ApprovalUiRuntime();
        var processes = new IdleProcesses();
        var shell = new AssistantShell(
            () => "instance-a",
            () => false,
            () => ("2026", "26.5.0.55"),
            processes,
            new IdleLink());
        shell.AttachApproval(ui);
        ui.RegisterPrimary(shell);
        var gate = new ScriptedGate { PrimarySucceeds = true };
        var adapter = new RevitLocalApprovalPresentationAdapter(controller, ui, gate);
        ui.Attach(adapter, controller);
        return new HarnessState(store, provider, controller, ui, adapter, gate, shell, processes, created.IntentRef!);
    }

    private static Func<EphemeralWriteIntentStore, IntentCreateResult> ParameterIntent()
    {
        return store => store.TryCreate(new IntentDraft
        {
            InstanceId = "instance-a",
            DocumentId = "doc-a",
            Items =
            [
                new IntentItemDraft
                {
                    RequestPosition = 1,
                    ElementRef = "element-a",
                    ParameterRef = "parameter-a",
                    Source = "instance",
                    IdentityKind = DescribeParameterIdentityKind.Local,
                    StableKey = "local:42",
                    Status = "ok",
                    ElementName = "Wall 1",
                    CategoryName = "Walls",
                    ParameterName = "Comments",
                    DataTypeKind = DescribeParameterDataTypeKind.Spec,
                    BeforeHasValue = true,
                    BeforeValue = new IntentTypedValue.StringValue("before"),
                    Proposed = new IntentTypedValue.StringValue("after"),
                },
            ],
        });
    }

    private static Func<EphemeralWriteIntentStore, IntentCreateResult> MepIntent()
    {
        return store => store.TryCreateMep("instance-a", "doc-a", new MepDevicePlacementIntent(
            MepPlacementStrategyKind.WallHosted,
            "autodesk.unit.unit:millimeters-1.0.1",
            "template-a",
            "symbol-a",
            "OST_ElectricalFixtures",
            "Electrical Fixtures",
            "Duplex Receptacle",
            "Standard",
            "level-a",
            "L4",
            new MepPoint(1, 0, 4),
            new MepOrientation(0, false, false, true, true),
            new MepWallBinding("wall-a", "Basic Wall", new MepPoint(0, 0, 0), new MepPoint(20, 0, 0)),
            null,
            [new MepDevicePlacementItem(1, 1200, 0, 0, new MepPoint(1200, 0, 0))]));
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

    private sealed record HarnessState(
        EphemeralWriteIntentStore Store,
        RevitLocalApprovalProviderStateMachine Provider,
        RevitLocalApprovalInteractionController Controller,
        ApprovalUiRuntime Ui,
        RevitLocalApprovalPresentationAdapter Adapter,
        ScriptedGate Gate,
        AssistantShell Shell,
        IdleProcesses Processes,
        string IntentRef);

    private sealed class RecordingSurface : IApprovalPaneSurface
    {
        public string Last { get; private set; } = "";

        public void BeginAfterShow()
        {
        }

        public void Post(string json) => Last = json;

        public void DisposeSurface()
        {
        }
    }

    private sealed class ScriptedGate : IApprovalRevitGate
    {
        public bool PrimarySucceeds { get; set; }

        public bool ShowSucceeds { get; set; } = true;

        public int PrimaryShows { get; private set; }

        public int FallbackShows { get; private set; }

        public Task<ApprovalPresentationResult> InvokeAsync(
            CancellationToken cancellationToken,
            Func<string?, Func<bool>, Func<bool>, ApprovalPresentationResult> present)
        {
            return Task.FromResult(present(
                "doc-a",
                () =>
                {
                    PrimaryShows++;
                    return PrimarySucceeds;
                },
                () =>
                {
                    FallbackShows++;
                    return ShowSucceeds;
                }));
        }

        public Task<ApprovalUiDispatchResult> DispatchAsync(
            CancellationToken cancellationToken,
            Func<string?, ApprovalUiDispatchResult> dispatch)
        {
            return Task.FromResult(dispatch("doc-a"));
        }
    }
}

internal sealed class IdleProcesses : IAssistantProcessFactory
{
    public int Starts { get; private set; }

    public IAssistantProcessHandle Start(string fileName, string arguments)
    {
        Starts++;
        throw new InvalidOperationException("The approval card must not start the Assistant process.");
    }
}

internal sealed class IdleLink : IAssistantLink
{
    public Task<AssistantIpcMessage?> ConnectAsync(string pipeName, string instanceId, string? revitVersion, string? revitBuild, bool bridgeReady, CancellationToken cancellationToken)
        => Task.FromResult<AssistantIpcMessage?>(null);

    public Task<AssistantIpcMessage?> RunAsync(string requestId, AssistantWorkflowName workflow, CancellationToken cancellationToken)
        => Task.FromResult<AssistantIpcMessage?>(null);

    public Task<AssistantIpcMessage?> ChatAsync(string requestId, string text, CancellationToken cancellationToken, Action<AssistantIpcMessage>? onActivity)
        => Task.FromResult<AssistantIpcMessage?>(null);

    public Task<AssistantIpcMessage?> ResetConversationAsync(CancellationToken cancellationToken)
        => Task.FromResult<AssistantIpcMessage?>(null);

    public Task<AssistantIpcMessage?> AttachDocumentAsync(string path, CancellationToken cancellationToken)
        => Task.FromResult<AssistantIpcMessage?>(null);

    public Task<AssistantIpcMessage?> RemoveDocumentAsync(CancellationToken cancellationToken)
        => Task.FromResult<AssistantIpcMessage?>(null);

    public Task<AssistantIpcMessage?> ConfirmProjectAxesPreviewAsync(CancellationToken cancellationToken)
        => Task.FromResult<AssistantIpcMessage?>(null);

    public Task<AssistantIpcMessage?> ReviewInRevitAsync(CancellationToken cancellationToken)
        => Task.FromResult<AssistantIpcMessage?>(null);

    public Task<AssistantIpcMessage?> ApplyApprovedPlacementsAsync(CancellationToken cancellationToken)
        => Task.FromResult<AssistantIpcMessage?>(null);

    public Task<AssistantIpcMessage?> PreparePatternPreviewAsync(string configuration, CancellationToken cancellationToken)
        => Task.FromResult<AssistantIpcMessage?>(null);

    public Task<AssistantIpcMessage?> SearchPatternTypesAsync(string role, string query, CancellationToken cancellationToken)
        => Task.FromResult<AssistantIpcMessage?>(null);

    public Task<AssistantIpcMessage?> ReviewPatternInRevitAsync(CancellationToken cancellationToken)
        => Task.FromResult<AssistantIpcMessage?>(null);

    public Task<AssistantIpcMessage?> ApplyApprovedPatternAsync(CancellationToken cancellationToken)
        => Task.FromResult<AssistantIpcMessage?>(null);

    public Task<AssistantIpcMessage?> QueryStatusAsync(CancellationToken cancellationToken)
        => Task.FromResult<AssistantIpcMessage?>(null);

    public Task<AssistantIpcMessage?> SkillsAsync(AssistantSkillsIpc.Request request, CancellationToken cancellationToken)
        => Task.FromResult<AssistantIpcMessage?>(null);

    public Task ShutdownAsync() => Task.CompletedTask;
}
