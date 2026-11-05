using System.Text;
using BimIt.Revit.Addin.Identity;
using BimIt.Revit.Addin.Query;
using BimIt.Revit.Bridge;
using BimIt.Revit.Contracts;
using Xunit;

namespace BimIt.Revit.Addin.Tests;

public sealed class DocumentQueryChangeTests
{
    // Live Revit 2026.5 (PR #100 validation): a DocumentChanged handler added to the stored startup
    // ControlledApplication during the Idling bootstrap received no event. Registration must happen
    // while OnStartup runs; the dispatcher lifetime subscribes to the relay later.
    [Fact]
    public void Revit_document_change_events_are_registered_during_OnStartup()
    {
        var root = RepoRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "BimIt.Revit.Addin", "Identity", "RevitDocumentQueryChangeEventSource.cs"));
        var constructor = Slice(source, "public RevitDocumentQueryChangeEventSource(ControlledApplication application)", "public IDisposable Subscribe(");
        Assert.Contains("application.DocumentChanged +=", constructor, StringComparison.Ordinal);
        Assert.Contains("application.DocumentSynchronizedWithCentral +=", constructor, StringComparison.Ordinal);
        Assert.Contains("application.DocumentReloadedLatest +=", constructor, StringComparison.Ordinal);
        var subscribe = source[source.IndexOf("public IDisposable Subscribe(", StringComparison.Ordinal)..];
        Assert.DoesNotContain("+=", subscribe, StringComparison.Ordinal);
        Assert.DoesNotContain("ControlledApplication _application", source, StringComparison.Ordinal);

        var application = File.ReadAllText(Path.Combine(root, "src", "BimIt.Revit.Addin", "RevitMcpApplication.cs"));
        var startup = Slice(application, "public Result OnStartup", "public Result OnShutdown");
        Assert.Contains("new RevitDocumentQueryChangeEventSource(application.ControlledApplication)", startup, StringComparison.Ordinal);

        var adapters = File.ReadAllText(Path.Combine(root, "src", "BimIt.Revit.Addin", "Lifecycle", "RevitLifecycleAdapters.cs"));
        Assert.Contains("_revisions.AdvanceIfKnown(document, _identity.TryGet)", adapters, StringComparison.Ordinal);
    }

    [Fact]
    public void Relay_delivers_signals_to_a_listener_that_subscribes_after_registration()
    {
        var relay = new DocumentQueryChangeRelay<object>();
        var document = new object();
        relay.Publish(document);

        var received = new List<object>();
        var subscription = relay.Subscribe(received.Add);
        relay.Publish(document);
        relay.Publish(document);
        Assert.Equal(2, received.Count);

        subscription.Dispose();
        subscription.Dispose();
        relay.Publish(document);
        Assert.Equal(2, received.Count);
    }

    [Fact]
    public void Known_document_signal_advances_and_unknown_document_signal_is_ignored()
    {
        var identity = new OpenDocumentIdentityMap<object>(() => "doc");
        var revisions = new DocumentQueryRevisionService();
        var relay = new DocumentQueryChangeRelay<object>();
        using var subscription = relay.Subscribe(document => revisions.AdvanceIfKnown(document, identity.TryGet));
        var host = new object();
        Assert.Equal("doc", identity.GetOrAssign(host));

        relay.Publish(host);
        Assert.Equal(1, revisions.Current("doc"));

        var unknown = new object();
        relay.Publish(unknown);
        Assert.False(revisions.AdvanceIfKnown(unknown, identity.TryGet));
        Assert.Equal(1, revisions.Current("doc"));
        Assert.False(identity.TryGet(unknown, out _));
    }

    [Fact]
    public void Signal_after_a_page_makes_the_cursor_stale_and_no_signal_continues()
    {
        var identity = new OpenDocumentIdentityMap<object>(() => "doc");
        var revisions = new DocumentQueryRevisionService();
        var relay = new DocumentQueryChangeRelay<object>();
        using var subscription = relay.Subscribe(document => revisions.AdvanceIfKnown(document, identity.TryGet));
        var host = new object();
        identity.GetOrAssign(host);
        var rows = new[] { Row("a"), Row("b"), Row("c") };

        var first = Execute(Request(cursor: null), rows, revisions.Current("doc"));
        Assert.NotNull(first.NextCursor);
        var second = Execute(Request(first.NextCursor), rows, revisions.Current("doc"));
        Assert.Equal("b", second.Types[0].TypeRef);

        relay.Publish(new object());
        var stillValid = Execute(Request(second.NextCursor), rows, revisions.Current("doc"));
        Assert.Equal("c", stillValid.Types[0].TypeRef);

        relay.Publish(host);
        var stale = Assert.Throws<BridgeException>(() => Execute(Request(second.NextCursor), rows, revisions.Current("doc")));
        Assert.Equal(CapabilityErrorCodes.StaleCursor, stale.ErrorCode);

        var restarted = Execute(Request(cursor: null), rows, revisions.Current("doc"));
        Assert.Equal("a", restarted.Types[0].TypeRef);
        Assert.NotEqual(first.NextCursor, restarted.NextCursor);
    }

    private static readonly DeclarativeQueryCursorCodec Cursors = new(Encoding.ASCII.GetBytes("0123456789abcdef0123456789abcdef"));

    private static QueryFamilyTypesResult Execute(QueryFamilyTypesRequest request, FamilyTypeCandidate[] rows, long revision)
    {
        return FamilyTypeInventoryQuery.Execute(
            FamilyTypeQueryValidator.Validate(request),
            "instance",
            "doc",
            hasActiveDocument: true,
            isFamilyDocument: false,
            revision,
            rows,
            new Categories(),
            Cursors);
    }

    private static QueryFamilyTypesRequest Request(string? cursor)
    {
        return new QueryFamilyTypesRequest
        {
            DocumentId = "doc",
            CategoryTokens = ["OST_Doors"],
            PageSize = 1,
            Cursor = cursor,
        };
    }

    private static FamilyTypeCandidate Row(string typeRef)
    {
        return new FamilyTypeCandidate(typeRef, "f", "OST_Doors", "Doors", "Alpha", typeRef.ToUpperInvariant(), "one_level_based", false, 0);
    }

    private sealed class Categories : IBuiltInCategoryCatalog
    {
        public bool IsDefinedMember(string token) => token == "OST_Doors";
    }

    private static string Slice(string text, string start, string end)
    {
        var from = text.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, "missing: " + start);
        var to = text.IndexOf(end, from + start.Length, StringComparison.Ordinal);
        Assert.True(to > from, "missing: " + end);
        return text[from..to];
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

        throw new InvalidOperationException("Repository root not found.");
    }
}
