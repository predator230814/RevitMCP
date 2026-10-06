using System.Buffers.Binary;
using System.Reflection;
using RevitMCP.Addin.Approval;
using RevitMCP.Addin.Identity;
using RevitMCP.Addin.Intents;
using RevitMCP.Addin.Lifecycle;
using RevitMCP.Contracts;
using Xunit;

namespace RevitMCP.Addin.Tests;

public sealed class ApprovalProviderLifecycleTests
{
    [Fact]
    public void Lifetime_owns_one_provider_beside_the_same_intent_store()
    {
        var store = Store(Bytes(1));
        using var lifetime = Lifetime(store);
        var created = store.TryCreate(Draft("doc-a"));

        var review = lifetime.ApprovalProvider.BeginReview(created.IntentRef, "doc-a");

        Assert.Equal(ApprovalReviewStatus.Started, review.Status);
        Assert.Equal(1, CountOccurrences(Adapters(), "new EphemeralWriteIntentStore()"));
        Assert.DoesNotContain("Autodesk.Revit", File.ReadAllText(StateMachinePath()), StringComparison.Ordinal);
    }

    [Fact]
    public void Successful_close_clears_provider_then_intent_then_refs_then_identity()
    {
        var store = Store(Bytes(2));
        var created = store.TryCreate(Draft("doc-a"));
        var provider = new RevitLocalApprovalProviderStateMachine(store);
        var started = provider.BeginReview(created.IntentRef, "doc-a");
        var steps = new List<string>();

        var forgotten = RevitExecutionDispatcherLifetime.ApplySuccessfulDocumentClose(
            true,
            "doc-a",
            documentId =>
            {
                var removed = provider.ForgetDocument(documentId);
                steps.Add(store.TryGet(created.IntentRef!, out _) ? "provider-before-intent" : "provider-after-intent");
                steps.Add(provider.ApproveCurrent(started.SessionRef, "doc-a").Status == ApprovalCommandStatus.InvalidSession
                    ? "session-cleared"
                    : "session-live");
                return removed;
            },
            documentId =>
            {
                steps.Add("intent");
                return store.ForgetDocument(documentId);
            },
            () =>
            {
                steps.Add(store.TryGet(created.IntentRef!, out _) ? "parameter-refs-while-live" : "parameter-refs");
                return true;
            },
            () =>
            {
                steps.Add("identity");
                return true;
            });

        Assert.True(forgotten);
        Assert.Equal(
            new[] { "provider-before-intent", "session-cleared", "intent", "parameter-refs", "identity" },
            steps);
        Assert.Equal(ApprovalReviewStatus.Unavailable, provider.BeginReview(created.IntentRef, "doc-a").Status);
    }

    [Fact]
    public void Missing_document_id_skips_provider_and_intent_forget()
    {
        var store = Store(Bytes(3));
        var created = store.TryCreate(Draft("doc-a"));
        var provider = new RevitLocalApprovalProviderStateMachine(store);
        var started = provider.BeginReview(created.IntentRef, "doc-a");
        var steps = new List<string>();

        RevitExecutionDispatcherLifetime.ApplySuccessfulDocumentClose(
            false,
            null,
            documentId =>
            {
                steps.Add("provider");
                return provider.ForgetDocument(documentId);
            },
            documentId =>
            {
                steps.Add("intent");
                return store.ForgetDocument(documentId);
            },
            () =>
            {
                steps.Add("parameter-refs");
                return true;
            },
            () =>
            {
                steps.Add("identity");
                return true;
            });

        Assert.Equal(new[] { "parameter-refs", "identity" }, steps);
        Assert.True(store.TryGet(created.IntentRef!, out _));
        Assert.Equal(started.SessionRef, provider.BeginReview(created.IntentRef, "doc-a").SessionRef);
    }

    [Fact]
    public void Cancelled_or_failed_close_preserves_provider_and_intent_state()
    {
        var store = Store(Bytes(4));
        var created = store.TryCreate(Draft("doc-a"));
        var provider = new RevitLocalApprovalProviderStateMachine(store);
        var started = provider.BeginReview(created.IntentRef, "doc-a");
        var cleanup = new DocumentCloseIdentityCleanup<object>(_ =>
            RevitExecutionDispatcherLifetime.ApplySuccessfulDocumentClose(
                true,
                "doc-a",
                provider.ForgetDocument,
                store.ForgetDocument,
                () => false,
                () => false));
        var document = new object();

        cleanup.OnClosing(1, document);
        cleanup.OnClosed(1, DocumentCloseOutcome.Cancelled);
        Assert.True(store.TryGet(created.IntentRef!, out _));
        Assert.Equal(started.SessionRef, provider.BeginReview(created.IntentRef, "doc-a").SessionRef);

        cleanup.OnClosing(2, document);
        cleanup.OnClosed(2, DocumentCloseOutcome.Failed);
        Assert.True(store.TryGet(created.IntentRef!, out _));
        Assert.Equal(ApprovalReviewStatus.AlreadyActive, provider.BeginReview(created.IntentRef, "doc-a").Status);

        cleanup.OnClosing(3, document);
        cleanup.OnClosed(3, DocumentCloseOutcome.Succeeded);
        Assert.False(store.TryGet(created.IntentRef!, out _));
        Assert.Equal(ApprovalReviewStatus.Unavailable, provider.BeginReview(created.IntentRef, "doc-a").Status);
    }

    [Fact]
    public void Stop_makes_the_provider_unavailable_before_the_intent_store_clears()
    {
        var store = Store(Bytes(5));
        var created = store.TryCreate(Draft("doc-a"));
        var steps = new List<string>();
        RevitExecutionDispatcherLifetime? lifetime = null;
        var observation = new CallbackDisposable(() =>
        {
            steps.Add(lifetime!.ApprovalProvider.BeginReview(created.IntentRef, "doc-a").Status == ApprovalReviewStatus.Unavailable
                ? "provider-unavailable"
                : "provider-live");
            steps.Add(store.TryGet(created.IntentRef!, out _) ? "intent-still-live" : "intent-cleared");
        });
        lifetime = Lifetime(store, () => steps.Add(store.TryGet(created.IntentRef!, out _) ? "execution-while-live" : "execution-after-clear"), observation);
        lifetime.ApprovalProvider.BeginReview(created.IntentRef, "doc-a");

        lifetime.Stop();

        Assert.Equal(new[] { "provider-unavailable", "intent-still-live", "execution-after-clear" }, steps);
        Assert.Equal(IntentStoreCreateStatus.Rejected, store.TryCreate(Draft("doc-b")).Status);
    }

    [Fact]
    public void Repeated_stop_and_dispose_detach_observation_once()
    {
        var store = Store(Bytes(6));
        var observation = new CallbackDisposable(() => { });
        var lifetime = Lifetime(store, () => { }, observation);

        lifetime.Stop();
        lifetime.Stop();
        lifetime.Dispose();
        lifetime.Dispose();

        Assert.Equal(1, observation.DisposeCount);
    }

    [Fact]
    public void Same_document_observation_retains_the_pending_session()
    {
        var store = Store(Bytes(7));
        var lifetime = Lifetime(store);
        var created = store.TryCreate(Draft("doc-a"));
        var started = lifetime.ApprovalProvider.BeginReview(created.IntentRef, "doc-a");

        lifetime.ObserveMappedActiveDocument(true, "doc-a");

        var again = lifetime.ApprovalProvider.BeginReview(created.IntentRef, "doc-a");
        Assert.Equal(ApprovalReviewStatus.AlreadyActive, again.Status);
        Assert.Equal(started.SessionRef, again.SessionRef);
    }

    [Fact]
    public void Different_or_unmapped_document_observation_ends_the_pending_session()
    {
        var differentStore = Store(Bytes(8));
        var different = Lifetime(differentStore);
        var first = differentStore.TryCreate(Draft("doc-a"));
        var started = different.ApprovalProvider.BeginReview(first.IntentRef, "doc-a");
        different.ObserveMappedActiveDocument(true, "doc-b");
        Assert.Equal(ApprovalCommandStatus.InvalidSession, different.ApprovalProvider.ApproveCurrent(started.SessionRef, "doc-a").Status);

        var unmappedStore = Store(Bytes(9));
        var unmapped = Lifetime(unmappedStore);
        var second = unmappedStore.TryCreate(Draft("doc-a"));
        var session = unmapped.ApprovalProvider.BeginReview(second.IntentRef, "doc-a");
        unmapped.ObserveMappedActiveDocument(false, null);
        Assert.Equal(ApprovalCommandStatus.InvalidSession, unmapped.ApprovalProvider.ApproveCurrent(session.SessionRef, "doc-a").Status);

        var suppliedStore = Store(Bytes(10));
        var suppliedButUnmapped = Lifetime(suppliedStore);
        var third = suppliedStore.TryCreate(Draft("doc-a"));
        var thirdSession = suppliedButUnmapped.ApprovalProvider.BeginReview(third.IntentRef, "doc-a");
        suppliedButUnmapped.ObserveMappedActiveDocument(false, "doc-a");
        Assert.Equal(ApprovalCommandStatus.InvalidSession, suppliedButUnmapped.ApprovalProvider.ApproveCurrent(thirdSession.SessionRef, "doc-a").Status);
    }

    [Fact]
    public void Active_document_adapter_uses_noncreating_tryget()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "RevitMCP.Addin", "Identity", "RevitActiveDocumentEventSource.cs"));
        var handler = Slice(source, "_handler = (_, args) =>", "_application.ViewActivated += _handler;");

        Assert.Contains("args.CurrentActiveView", handler, StringComparison.Ordinal);
        Assert.Contains("identity.TryGet(document, out var documentId)", handler, StringComparison.Ordinal);
        Assert.Contains("ViewActivated", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".GetId(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetOrAssign(", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".GetId(", Adapters(), StringComparison.Ordinal);
        Assert.DoesNotContain("GetOrAssign(", Slice(Adapters(), "private void OnActiveDocumentChanged", "private void DetachActiveDocumentObservation"), StringComparison.Ordinal);
    }

    [Fact]
    public void Observation_subscription_failure_disables_the_provider_without_clearing_intents()
    {
        var store = Store(Bytes(15), Bytes(16));
        var created = store.TryCreate(Draft("doc-a"));
        var lifetime = new RevitExecutionDispatcherLifetime(
            store,
            () => { },
            () => { },
            closeCleanup: null,
            activeDocumentEvents: new ThrowingActiveDocumentSource());

        Assert.Equal(ApprovalReviewStatus.Unavailable, lifetime.ApprovalProvider.BeginReview(created.IntentRef, "doc-a").Status);
        Assert.True(store.TryGet(created.IntentRef!, out _));
        Assert.Equal(IntentStoreCreateStatus.Created, store.TryCreate(Draft("doc-b")).Status);

        var application = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "RevitMCP.Addin", "RevitMcpApplication.cs"));
        var startup = Slice(application, "public Result OnStartup", "public Result OnShutdown");
        var isolated = startup.IndexOf("new RevitActiveDocumentEventSource(application)", StringComparison.Ordinal);
        var disabled = startup.IndexOf("new DisabledActiveDocumentEventSource()", StringComparison.Ordinal);
        var failed = startup.IndexOf("return Result.Failed", StringComparison.Ordinal);
        Assert.True(isolated >= 0 && isolated < disabled && disabled < failed);
    }

    [Fact]
    public void Provider_state_retains_no_revit_api_wrappers()
    {
        foreach (var type in new[] { typeof(RevitLocalApprovalProviderStateMachine) }.Concat(
                     typeof(RevitLocalApprovalProviderStateMachine).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)))
        {
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                Assert.DoesNotContain("Autodesk.Revit", field.FieldType.FullName ?? string.Empty, StringComparison.Ordinal);
            }
        }
    }

    private static RevitExecutionDispatcherLifetime Lifetime(
        EphemeralWriteIntentStore store,
        Action? stopExecution = null,
        IDisposable? observation = null)
    {
        return new RevitExecutionDispatcherLifetime(
            store,
            stopExecution ?? (() => { }),
            () => { },
            closeCleanup: null,
            activeDocumentEvents: observation is null ? null : new CallbackActiveDocumentSource(observation));
    }

    private static EphemeralWriteIntentStore Store(params byte[][] draws)
    {
        var queue = new Queue<byte[]>(draws);
        return new EphemeralWriteIntentStore(randomBytes: queue.Dequeue);
    }

    private static IntentDraft Draft(string documentId)
    {
        return new IntentDraft
        {
            InstanceId = "instance-a",
            DocumentId = documentId,
            Items = new List<IntentItemDraft>
            {
                new()
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
                    Proposed = new IntentTypedValue.StringValue("proposed")
                }
            }
        };
    }

    private static byte[] Bytes(byte marker)
    {
        var bytes = new byte[32];
        BinaryPrimitives.WriteInt32BigEndian(bytes, marker);
        bytes[4] = marker;
        return bytes;
    }

    private static string Adapters()
    {
        return File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "RevitMCP.Addin", "Lifecycle", "RevitLifecycleAdapters.cs"));
    }

    private static string StateMachinePath()
    {
        return Path.Combine(FindRepoRoot(), "src", "RevitMCP.Addin", "Approval", "RevitLocalApprovalProviderStateMachine.cs");
    }

    private static string Slice(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        var endIndex = source.IndexOf(end, startIndex + start.Length, StringComparison.Ordinal);
        Assert.True(startIndex >= 0 && endIndex > startIndex);
        return source[startIndex..endIndex];
    }

    private static int CountOccurrences(string text, string value)
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

    private static string FindRepoRoot()
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

        throw new InvalidOperationException("Could not locate RevitMCP.sln from the test output directory.");
    }

    private sealed class CallbackActiveDocumentSource : IActiveDocumentEventSource
    {
        private readonly IDisposable _subscription;

        public CallbackActiveDocumentSource(IDisposable subscription)
        {
            _subscription = subscription;
        }

        public IDisposable Subscribe(ActiveDocumentChanged onChanged, OpenDocumentIdentityService identity)
        {
            return _subscription;
        }
    }

    private sealed class ThrowingActiveDocumentSource : IActiveDocumentEventSource
    {
        public IDisposable Subscribe(ActiveDocumentChanged onChanged, OpenDocumentIdentityService identity)
        {
            throw new InvalidOperationException("Active-document observation is unavailable.");
        }
    }

    private sealed class CallbackDisposable : IDisposable
    {
        private readonly Action _onDispose;
        private int _disposed;

        public CallbackDisposable(Action onDispose)
        {
            _onDispose = onDispose;
        }

        public int DisposeCount { get; private set; }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            DisposeCount++;
            _onDispose();
        }
    }
}
