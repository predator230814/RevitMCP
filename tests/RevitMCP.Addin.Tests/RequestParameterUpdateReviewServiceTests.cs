using System.Buffers.Binary;
using System.Reflection;
using RevitMCP.Addin.Approval;
using RevitMCP.Addin.Capabilities;
using RevitMCP.Addin.Intents;
using RevitMCP.Addin.Lifecycle;
using RevitMCP.Bridge;
using RevitMCP.Contracts;
using Xunit;

namespace RevitMCP.Addin.Tests;

public sealed class RequestParameterUpdateReviewServiceTests
{
    [Fact]
    public void Request_service_uses_the_runtime_attached_to_the_lifetime()
    {
        var store = new EphemeralWriteIntentStore();
        var lifetime = new RevitExecutionDispatcherLifetime(store, static () => { }, static () => { });
        var metadata = Metadata();
        Assert.Null(lifetime.CreateRequestParameterUpdateReview(metadata));

        var ui = new ApprovalUiRuntime();
        typeof(RevitExecutionDispatcherLifetime)
            .GetField("_approvalUi", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(lifetime, ui);
        var service = Assert.IsType<RevitRequestParameterUpdateReviewService>(lifetime.CreateRequestParameterUpdateReview(metadata));
        Assert.Same(ui, service.Runtime);
    }

    [Fact]
    public void Lifetime_does_not_create_a_second_store_provider_controller_or_ui_runtime()
    {
        var adapters = File.ReadAllText(Path.Combine(RepoRoot(), "src", "RevitMCP.Addin", "Lifecycle", "RevitLifecycleAdapters.cs"));
        var application = File.ReadAllText(Path.Combine(RepoRoot(), "src", "RevitMCP.Addin", "RevitMcpApplication.cs"));
        var service = File.ReadAllText(Path.Combine(RepoRoot(), "src", "RevitMCP.Addin", "Approval", "RevitRequestParameterUpdateReviewService.cs"));

        Assert.Equal(1, Count(adapters, "new EphemeralWriteIntentStore()"));
        Assert.Equal(2, Count(adapters, "new RevitLocalApprovalProviderStateMachine("));
        Assert.Equal(2, Count(adapters, "new RevitLocalApprovalInteractionController("));
        Assert.Equal(1, Count(application, "new ApprovalUiRuntime()"));
        Assert.DoesNotContain("new EphemeralWriteIntentStore", service, StringComparison.Ordinal);
        Assert.DoesNotContain("new RevitLocalApprovalProviderStateMachine", service, StringComparison.Ordinal);
        Assert.DoesNotContain("new RevitLocalApprovalInteractionController", service, StringComparison.Ordinal);
        Assert.DoesNotContain("new ApprovalUiRuntime", service, StringComparison.Ordinal);
        Assert.Contains("new RevitRequestParameterUpdateReviewService(_approvalUi)", adapters, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Started_maps_to_started()
    {
        var harness = Harness();
        var result = await harness.Service.RequestParameterUpdateReviewAsync(Request(harness.IntentRef), CancellationToken.None);
        Assert.Equal(RequestParameterUpdateReviewStatus.Started, result.Status);
        Assert.NotNull(harness.Ui.Current);
    }

    [Fact]
    public async Task Already_active_maps_to_already_active()
    {
        var harness = Harness();
        await harness.Service.RequestParameterUpdateReviewAsync(Request(harness.IntentRef), CancellationToken.None);
        var repeated = await harness.Service.RequestParameterUpdateReviewAsync(Request(harness.IntentRef), CancellationToken.None);
        Assert.Equal(RequestParameterUpdateReviewStatus.AlreadyActive, repeated.Status);
        Assert.Equal(2, harness.Gate.PresentEntries);
    }

    [Fact]
    public async Task Busy_maps_to_busy()
    {
        var harness = Harness();
        var other = harness.Store.TryCreate(Draft("doc-a", 2));
        Assert.Equal(IntentStoreCreateStatus.Created, other.Status);
        await harness.Service.RequestParameterUpdateReviewAsync(Request(harness.IntentRef), CancellationToken.None);
        var busy = await harness.Service.RequestParameterUpdateReviewAsync(Request(other.IntentRef!), CancellationToken.None);
        Assert.Equal(RequestParameterUpdateReviewStatus.Busy, busy.Status);
    }

    [Fact]
    public async Task Unavailable_maps_to_unavailable()
    {
        var harness = Harness();
        harness.Gate.ShowSucceeds = false;
        var result = await harness.Service.RequestParameterUpdateReviewAsync(Request(harness.IntentRef), CancellationToken.None);
        Assert.Equal(RequestParameterUpdateReviewStatus.Unavailable, result.Status);
    }

    [Fact]
    public async Task Terminal_maps_to_terminal_without_revealing_the_decision()
    {
        var harness = Harness();
        var started = await harness.Service.RequestParameterUpdateReviewAsync(Request(harness.IntentRef), CancellationToken.None);
        Assert.Equal(RequestParameterUpdateReviewStatus.Started, started.Status);
        Assert.Equal(ApprovalCommandStatus.Recorded, harness.Controller.ApproveCurrent(harness.Ui.Current!.SessionRef, "doc-a").Status);

        var terminal = await harness.Service.RequestParameterUpdateReviewAsync(Request(harness.IntentRef), CancellationToken.None);
        Assert.Equal(RequestParameterUpdateReviewStatus.Terminal, terminal.Status);
        Assert.Equal(ApprovalConsumeStatus.Consumed, harness.Provider.TryConsumeApproved(harness.IntentRef).Status);
    }

    [Fact]
    public async Task Unknown_intent_is_unavailable_without_a_not_found_error()
    {
        var harness = Harness();
        var result = await harness.Service.RequestParameterUpdateReviewAsync(Request("missing-intent"), CancellationToken.None);
        Assert.Equal(RequestParameterUpdateReviewStatus.Unavailable, result.Status);
    }

    [Fact]
    public async Task Wrong_active_document_is_unavailable()
    {
        var harness = Harness();
        harness.Gate.DocumentId = "other-doc";
        var result = await harness.Service.RequestParameterUpdateReviewAsync(Request(harness.IntentRef), CancellationToken.None);
        Assert.Equal(RequestParameterUpdateReviewStatus.Unavailable, result.Status);
        Assert.Equal(ApprovalConsumeStatus.NotApproved, harness.Provider.TryConsumeApproved(harness.IntentRef).Status);
    }

    [Fact]
    public async Task Expired_intent_is_unavailable()
    {
        var harness = Harness();
        harness.Clock.Advance(EphemeralWriteIntentStore.Lifetime + TimeSpan.FromSeconds(1));
        var result = await harness.Service.RequestParameterUpdateReviewAsync(Request(harness.IntentRef), CancellationToken.None);
        Assert.Equal(RequestParameterUpdateReviewStatus.Unavailable, result.Status);
    }

    [Fact]
    public async Task Unavailable_ui_returns_unavailable_and_records_no_decision()
    {
        var harness = Harness();
        harness.Ui.MarkUnavailable();
        var result = await harness.Service.RequestParameterUpdateReviewAsync(Request(harness.IntentRef), CancellationToken.None);
        Assert.Equal(RequestParameterUpdateReviewStatus.Unavailable, result.Status);
        Assert.Equal(ApprovalConsumeStatus.NotApproved, harness.Provider.TryConsumeApproved(harness.IntentRef).Status);
        Assert.Equal(ApprovalCommandStatus.InvalidSession, harness.Controller.ApproveCurrent("session~1", "doc-a").Status);
    }

    [Fact]
    public async Task Stopped_provider_returns_unavailable_and_is_not_resurrected()
    {
        var harness = Harness();
        harness.Provider.Stop();
        var first = await harness.Service.RequestParameterUpdateReviewAsync(Request(harness.IntentRef), CancellationToken.None);
        var second = await harness.Service.RequestParameterUpdateReviewAsync(Request(harness.IntentRef), CancellationToken.None);
        Assert.Equal(RequestParameterUpdateReviewStatus.Unavailable, first.Status);
        Assert.Equal(RequestParameterUpdateReviewStatus.Unavailable, second.Status);
        Assert.Equal(ApprovalReviewStatus.Unavailable, harness.Provider.BeginReview(harness.IntentRef, "doc-a").Status);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("bad ref")]
    public async Task Malformed_request_is_a_dedicated_capability_error(string intentRef)
    {
        var harness = Harness();
        var exception = await Assert.ThrowsAsync<BridgeException>(() =>
            harness.Service.RequestParameterUpdateReviewAsync(Request(intentRef), CancellationToken.None));
        Assert.Equal(CapabilityErrorCodes.InvalidApprovalReviewRequest, exception.ErrorCode);
        Assert.Equal(0, harness.Gate.PresentEntries);
    }

    [Fact]
    public async Task Request_does_not_consume_or_record_a_decision()
    {
        var harness = Harness();
        var result = await harness.Service.RequestParameterUpdateReviewAsync(Request(harness.IntentRef), CancellationToken.None);
        Assert.Equal(RequestParameterUpdateReviewStatus.Started, result.Status);
        Assert.Equal(ApprovalConsumeStatus.NotApproved, harness.Provider.TryConsumeApproved(harness.IntentRef).Status);
        Assert.Equal(ApprovalCommandStatus.Recorded, harness.Controller.ApproveCurrent(harness.Ui.Current!.SessionRef, "doc-a").Status);
    }

    [Fact]
    public void Request_service_does_not_retain_revit_wrappers_and_preview_stays_independent()
    {
        var service = File.ReadAllText(Path.Combine(RepoRoot(), "src", "RevitMCP.Addin", "Approval", "RevitRequestParameterUpdateReviewService.cs"));
        var preview = File.ReadAllText(Path.Combine(RepoRoot(), "src", "RevitMCP.Addin", "Capabilities", "RevitPreviewParameterUpdatesService.cs"));
        var adapters = File.ReadAllText(Path.Combine(RepoRoot(), "src", "RevitMCP.Addin", "Lifecycle", "RevitLifecycleAdapters.cs"));

        Assert.DoesNotContain("Autodesk.Revit", service, StringComparison.Ordinal);
        Assert.DoesNotContain("UIApplication", service, StringComparison.Ordinal);
        Assert.DoesNotContain("TryConsumeApproved", service, StringComparison.Ordinal);
        Assert.DoesNotContain("Transaction", service, StringComparison.Ordinal);
        Assert.DoesNotContain("Parameter.Set", service, StringComparison.Ordinal);
        Assert.DoesNotContain("ApprovalUiRuntime", preview, StringComparison.Ordinal);
        Assert.Contains("new RevitPreviewParameterUpdatesService(_dispatcher, metadata, _identity, _parameterRefs, _intentStore)", adapters, StringComparison.Ordinal);
        Assert.DoesNotContain(
            typeof(RevitPreviewParameterUpdatesService).GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic),
            constructor => constructor.GetParameters().Any(parameter => parameter.ParameterType == typeof(ApprovalUiRuntime)));
        Assert.Equal(
            [typeof(ApprovalUiRuntime)],
            typeof(RevitRequestParameterUpdateReviewService)
                .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
                .Select(field => field.FieldType)
                .ToArray());
    }

    private static HarnessState Harness()
    {
        var next = 0;
        var clock = new ManualTimeProvider();
        var store = new EphemeralWriteIntentStore(clock, () => Bytes((byte)Interlocked.Increment(ref next)));
        var provider = new RevitLocalApprovalProviderStateMachine(store, clock, () => "session");
        var controller = new RevitLocalApprovalInteractionController(store, provider);
        var ui = new ApprovalUiRuntime();
        var gate = new ScriptedGate();
        var adapter = new RevitLocalApprovalPresentationAdapter(controller, ui, gate);
        ui.Attach(adapter, controller);
        ui.RegisterSurface(new RecordingSurface());
        var created = store.TryCreate(Draft("doc-a", 1));
        Assert.Equal(IntentStoreCreateStatus.Created, created.Status);
        return new HarnessState(clock, store, provider, controller, ui, gate, new RevitRequestParameterUpdateReviewService(ui), created.IntentRef!);
    }

    private static RequestParameterUpdateReviewRequest Request(string intentRef)
    {
        return new RequestParameterUpdateReviewRequest { IntentRef = intentRef };
    }

    private static IntentDraft Draft(string documentId, byte marker)
    {
        return new IntentDraft
        {
            InstanceId = "instance-a",
            DocumentId = documentId,
            Items =
            [
                new IntentItemDraft
                {
                    RequestPosition = 1,
                    ElementRef = "element-" + marker,
                    ParameterRef = "parameter-" + marker,
                    Source = "instance",
                    IdentityKind = DescribeParameterIdentityKind.Local,
                    StableKey = "local:" + marker,
                    Status = "ok",
                    ElementName = "Wall",
                    CategoryName = "Walls",
                    ParameterName = "Comments",
                    DataTypeKind = DescribeParameterDataTypeKind.Spec,
                    BeforeHasValue = false,
                    Proposed = new IntentTypedValue.StringValue("proposed")
                }
            ]
        };
    }

    private static byte[] Bytes(byte marker)
    {
        var bytes = new byte[32];
        BinaryPrimitives.WriteInt32BigEndian(bytes, marker);
        bytes[4] = marker;
        return bytes;
    }

    private static BridgeInstanceMetadata Metadata()
    {
        return new BridgeInstanceMetadata
        {
            InstanceId = "instance-a",
            ProcessId = 1,
            ProcessStartTimeUtc = DateTimeOffset.UnixEpoch,
            WindowsSessionId = 1,
            RevitVersion = "2026",
            RevitBuild = "26.5.0.55",
            AddinVersion = "1.0.0",
            SupportedProtocolVersions = BridgeProtocol.SupportedVersions
        };
    }

    private static int Count(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "RevitMCP.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root was not found.");
    }

    private sealed class RecordingSurface : IApprovalPaneSurface
    {
        public void BeginAfterShow()
        {
        }

        public void Post(string json)
        {
        }

        public void DisposeSurface()
        {
        }
    }

    private sealed class ScriptedGate : IApprovalRevitGate
    {
        public string? DocumentId { get; set; } = "doc-a";

        public bool ShowSucceeds { get; set; } = true;

        public int PresentEntries { get; private set; }

        public Task<ApprovalPresentationResult> InvokeAsync(
            CancellationToken cancellationToken,
            Func<string?, Func<bool>, ApprovalPresentationResult> present)
        {
            PresentEntries++;
            return Task.FromResult(present(DocumentId, () => ShowSucceeds));
        }

        public Task<ApprovalUiDispatchResult> DispatchAsync(
            CancellationToken cancellationToken,
            Func<string?, ApprovalUiDispatchResult> dispatch)
        {
            return Task.FromResult(dispatch(DocumentId));
        }
    }

    private sealed record HarnessState(
        ManualTimeProvider Clock,
        EphemeralWriteIntentStore Store,
        RevitLocalApprovalProviderStateMachine Provider,
        RevitLocalApprovalInteractionController Controller,
        ApprovalUiRuntime Ui,
        ScriptedGate Gate,
        RevitRequestParameterUpdateReviewService Service,
        string IntentRef);
}
