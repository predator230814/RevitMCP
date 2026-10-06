using System.Buffers.Binary;
using RevitMCP.Addin.Identity;
using RevitMCP.Addin.Intents;
using RevitMCP.Addin.Lifecycle;
using RevitMCP.Contracts;
using Xunit;

namespace RevitMCP.Addin.Tests;

public sealed class IntentStoreLifecycleTests
{
    [Fact]
    public void Lifetime_uses_the_injected_store()
    {
        var store = Store(Bytes(1));
        var steps = new List<string>();
        var liveRefs = new List<string>();
        using var lifetime = Lifetime(store, steps, liveRefs);
        var created = store.TryCreate(Draft("doc-a"));
        liveRefs.Add(created.IntentRef!);

        Assert.Equal(IntentStoreCreateStatus.Created, created.Status);
        Assert.True(store.TryGet(created.IntentRef!, out var stored));
        Assert.Equal("doc-a", stored!.DocumentId);

        lifetime.Stop();

        Assert.Equal(new[] { "stop-after-clear" }, steps);
        Assert.False(store.TryGet(created.IntentRef!, out _));
        Assert.Equal(IntentStoreCreateStatus.Rejected, store.TryCreate(Draft("doc-a")).Status);
    }

    [Fact]
    public void Stop_removes_existing_intents_and_rejects_later_create()
    {
        var store = Store(Bytes(2), Bytes(3));
        var steps = new List<string>();
        var liveRefs = new List<string>();
        var lifetime = Lifetime(store, steps, liveRefs);
        var first = store.TryCreate(Draft("doc-a"));
        var second = store.TryCreate(Draft("doc-b"));
        liveRefs.Add(first.IntentRef!);
        liveRefs.Add(second.IntentRef!);
        Assert.True(store.TryGet(first.IntentRef!, out _));
        Assert.True(store.TryGet(second.IntentRef!, out _));

        lifetime.Stop();

        Assert.Equal(new[] { "stop-after-clear" }, steps);
        Assert.False(store.TryGet(first.IntentRef!, out _));
        Assert.False(store.TryGet(second.IntentRef!, out _));
        Assert.Equal(IntentStoreCreateStatus.Rejected, store.TryCreate(Draft("doc-c")).Status);
        lifetime.Dispose();
    }

    [Fact]
    public void Dispose_without_stop_closes_the_store_and_unsubscribes_once()
    {
        var store = Store(Bytes(4));
        var steps = new List<string>();
        var liveRefs = new List<string>();
        var cleanup = new RecordingDisposable();
        var lifetime = Lifetime(store, steps, liveRefs, cleanup);
        var created = store.TryCreate(Draft("doc-a"));
        liveRefs.Add(created.IntentRef!);

        lifetime.Dispose();

        Assert.Equal(new[] { "stop-after-clear", "dispose-execution" }, steps);
        Assert.False(store.TryGet(created.IntentRef!, out _));
        Assert.Equal(IntentStoreCreateStatus.Rejected, store.TryCreate(Draft("doc-b")).Status);
        Assert.Equal(1, cleanup.DisposeCount);
    }

    [Fact]
    public void Repeated_stop_and_dispose_remain_harmless()
    {
        var store = Store(Bytes(5));
        var steps = new List<string>();
        var liveRefs = new List<string>();
        var cleanup = new RecordingDisposable();
        var lifetime = Lifetime(store, steps, liveRefs, cleanup);
        var created = store.TryCreate(Draft("doc-a"));
        liveRefs.Add(created.IntentRef!);

        lifetime.Stop();
        lifetime.Stop();
        lifetime.Dispose();
        lifetime.Dispose();

        Assert.Equal(
            new[] { "stop-after-clear", "stop-after-clear", "stop-after-clear", "dispose-execution" },
            steps);
        Assert.Equal(1, cleanup.DisposeCount);
        Assert.False(store.TryGet(created.IntentRef!, out _));
        Assert.Equal(IntentStoreCreateStatus.Rejected, store.TryCreate(Draft("doc-b")).Status);
    }

    [Fact]
    public void Successful_close_forgets_intents_before_parameter_refs_and_identity()
    {
        var store = Store(Bytes(6), Bytes(7));
        var matching = store.TryCreate(Draft("doc-a"));
        var other = store.TryCreate(Draft("doc-b"));
        var steps = new List<string>();

        var provider = new RevitMCP.Addin.Approval.RevitLocalApprovalProviderStateMachine(store);
        var forgotten = RevitExecutionDispatcherLifetime.ApplySuccessfulDocumentClose(
            hasExistingDocumentId: true,
            documentId: "doc-a",
            provider.ForgetDocument,
            store.ForgetDocument,
            () =>
            {
                steps.Add(store.TryGet(matching.IntentRef!, out _) ? "parameter-refs-while-live" : "parameter-refs");
                return true;
            },
            () =>
            {
                steps.Add("identity");
                return true;
            });

        Assert.True(forgotten);
        Assert.Equal(new[] { "parameter-refs", "identity" }, steps);
        Assert.False(store.TryGet(matching.IntentRef!, out _));
        Assert.True(store.TryGet(other.IntentRef!, out var remaining));
        Assert.Equal("doc-b", remaining!.DocumentId);
    }

    [Fact]
    public void Missing_document_id_skips_intent_forget_and_still_clears_bookkeeping()
    {
        var store = Store(Bytes(8));
        var created = store.TryCreate(Draft("doc-a"));
        var steps = new List<string>();

        var provider = new RevitMCP.Addin.Approval.RevitLocalApprovalProviderStateMachine(store);
        var forgotten = RevitExecutionDispatcherLifetime.ApplySuccessfulDocumentClose(
            hasExistingDocumentId: false,
            documentId: null,
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
                return false;
            },
            () =>
            {
                steps.Add("identity");
                return true;
            });

        Assert.True(forgotten);
        Assert.Equal(new[] { "parameter-refs", "identity" }, steps);
        Assert.True(store.TryGet(created.IntentRef!, out _));
    }

    [Fact]
    public void Cancelled_or_failed_close_preserves_intents()
    {
        var store = Store(Bytes(9));
        var created = store.TryCreate(Draft("doc-a"));
        var cleanup = new DocumentCloseIdentityCleanup<object>(_ =>
        {
            var provider = new RevitMCP.Addin.Approval.RevitLocalApprovalProviderStateMachine(store);
            return RevitExecutionDispatcherLifetime.ApplySuccessfulDocumentClose(
                true,
                "doc-a",
                provider.ForgetDocument,
                store.ForgetDocument,
                () => false,
                () => false);
        });
        var document = new object();

        cleanup.OnClosing(1, document);
        cleanup.OnClosed(1, DocumentCloseOutcome.Cancelled);
        Assert.True(store.TryGet(created.IntentRef!, out _));
        Assert.False(cleanup.HasPending(1));

        cleanup.OnClosing(2, document);
        cleanup.OnClosed(2, DocumentCloseOutcome.Failed);
        Assert.True(store.TryGet(created.IntentRef!, out _));
        Assert.False(cleanup.HasPending(2));

        cleanup.OnClosing(3, document);
        cleanup.OnClosed(3, DocumentCloseOutcome.Succeeded);
        Assert.False(store.TryGet(created.IntentRef!, out _));
        Assert.False(cleanup.HasPending(3));
    }

    [Fact]
    public void Production_close_wiring_uses_noncreating_tryget_before_identity_forget()
    {
        var adapters = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "RevitMCP.Addin", "Lifecycle", "RevitLifecycleAdapters.cs"));
        var coordinator = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "RevitMCP.Addin", "Lifecycle", "AddinLifecycleCoordinator.cs"));

        Assert.Equal(1, CountOccurrences(adapters, "new EphemeralWriteIntentStore()"));
        Assert.Contains("new RevitExecutionDispatcherLifetime(RevitExecutionDispatcher.Create(), CloseEvents, ActiveDocumentEvents)", adapters, StringComparison.Ordinal);
        Assert.Contains("new DocumentCloseIdentityCleanup<Autodesk.Revit.DB.Document>(ForgetDocument)", adapters, StringComparison.Ordinal);

        var forget = Slice(adapters, "private bool ForgetDocument(", "internal static bool ApplySuccessfulDocumentClose");
        var tryGet = forget.IndexOf("_identity.TryGet(document, out var documentId)", StringComparison.Ordinal);
        var parameterForget = forget.IndexOf("() => _parameterRefs.Forget(document)", StringComparison.Ordinal);
        var identityForget = forget.IndexOf("() => _identity.Forget(document)", StringComparison.Ordinal);
        Assert.True(tryGet >= 0 && tryGet < parameterForget && parameterForget < identityForget);
        Assert.DoesNotContain(".GetId(", forget, StringComparison.Ordinal);
        Assert.DoesNotContain("GetOrAssign(", forget, StringComparison.Ordinal);

        var apply = Slice(adapters, "internal static bool ApplySuccessfulDocumentClose", "public void Stop()");
        var providerForget = apply.IndexOf("forgetProvider(documentId)", StringComparison.Ordinal);
        var intentForget = apply.IndexOf("forgetIntent(documentId)", StringComparison.Ordinal);
        var parameterCallback = apply.IndexOf("forgetParameterRefs()", StringComparison.Ordinal);
        var identityCallback = apply.IndexOf("forgetDocumentIdentity()", StringComparison.Ordinal);
        Assert.Contains("if (hasExistingDocumentId)", apply, StringComparison.Ordinal);
        Assert.True(providerForget >= 0 && providerForget < intentForget && intentForget < parameterCallback && parameterCallback < identityCallback);
        var providerBinding = forget.IndexOf("_approval.ForgetDocument", StringComparison.Ordinal);
        var intentBinding = forget.IndexOf("_intentStore.ForgetDocument", StringComparison.Ordinal);
        Assert.True(providerBinding >= 0 && providerBinding < intentBinding);

        var production = Slice(adapters, "internal RevitExecutionDispatcherLifetime(", "internal RevitExecutionDispatcherLifetime(");
        Assert.Contains("BindExecution(dispatcher)", production, StringComparison.Ordinal);
        var binding = Slice(adapters, "private static (Action Stop, Action Dispose, RevitExecutionDispatcher Dispatcher) BindExecution", "internal RevitExecutionDispatcherLifetime(");
        var stopBinding = binding.IndexOf("dispatcher.Stop", StringComparison.Ordinal);
        var disposeBinding = binding.IndexOf("dispatcher.Dispose", StringComparison.Ordinal);
        Assert.True(stopBinding >= 0 && stopBinding < disposeBinding);

        var stop = Slice(adapters, "public void Stop()", "public void Dispose()");
        var providerStop = stop.IndexOf("_approval.Stop()", StringComparison.Ordinal);
        var detach = stop.IndexOf("DetachActiveDocumentObservation()", StringComparison.Ordinal);
        var clear = stop.IndexOf("_intentStore.Clear()", StringComparison.Ordinal);
        var executionStop = stop.IndexOf("_stopExecution()", StringComparison.Ordinal);
        Assert.True(providerStop >= 0 && providerStop < detach && detach < clear && clear < executionStop);

        var dispose = Slice(adapters, "public void Dispose()", "public IRevitCapabilityService");
        var stopCall = dispose.IndexOf("Stop();", StringComparison.Ordinal);
        var unsubscribe = dispose.IndexOf("_closeCleanup?.Dispose();", StringComparison.Ordinal);
        var dispatcherDispose = dispose.IndexOf("_disposeExecution();", StringComparison.Ordinal);
        Assert.True(stopCall >= 0 && stopCall < unsubscribe && unsubscribe < dispatcherDispose);

        var shutdown = Slice(coordinator, "public void Shutdown()", "private void EnsureNotStopping()");
        var dispatcherStop = shutdown.IndexOf("dispatcher?.Stop();", StringComparison.Ordinal);
        var bridge = shutdown.IndexOf("DisposeBridgeBounded(bridge);", StringComparison.Ordinal);
        var dispatcherDisposeCall = shutdown.IndexOf("dispatcher?.Dispose();", StringComparison.Ordinal);
        Assert.True(dispatcherStop >= 0 && dispatcherStop < bridge && bridge < dispatcherDisposeCall);
    }

    private static RevitExecutionDispatcherLifetime Lifetime(
        EphemeralWriteIntentStore store,
        List<string> steps,
        List<string> liveRefs,
        RecordingDisposable? cleanup = null)
    {
        return new RevitExecutionDispatcherLifetime(
            store,
            () => steps.Add(liveRefs.Any(intentRef => store.TryGet(intentRef, out _)) ? "stop-while-live" : "stop-after-clear"),
            () => steps.Add("dispose-execution"),
            cleanup);
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

    private sealed class RecordingDisposable : IDisposable
    {
        public int DisposeCount { get; private set; }

        public void Dispose()
        {
            DisposeCount++;
        }
    }
}
