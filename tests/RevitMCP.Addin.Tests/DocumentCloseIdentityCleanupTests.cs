using RevitMCP.Addin.Identity;
using Xunit;

namespace RevitMCP.Addin.Tests;

public sealed class DocumentCloseIdentityCleanupTests
{
    [Fact]
    public void Closing_then_closed_succeeded_forgets_the_mapping()
    {
        var map = new OpenDocumentIdentityMap<object>();
        var cleanup = new DocumentCloseIdentityCleanup<object>(map.Remove);
        var document = new object();
        var id = map.GetOrAssign(document);

        cleanup.OnClosing(12, document);
        cleanup.OnClosed(12, DocumentCloseOutcome.Succeeded);

        Assert.False(map.TryGet(document, out _));
        Assert.False(cleanup.HasPending(12));
        Assert.NotEqual(id, map.GetOrAssign(document));
    }

    [Fact]
    public void Closing_then_closed_cancelled_preserves_the_mapping()
    {
        var map = new OpenDocumentIdentityMap<object>();
        var cleanup = new DocumentCloseIdentityCleanup<object>(map.Remove);
        var document = new object();
        var id = map.GetOrAssign(document);

        cleanup.OnClosing(8, document);
        cleanup.OnClosed(8, DocumentCloseOutcome.Cancelled);

        Assert.True(map.TryGet(document, out var preserved));
        Assert.Equal(id, preserved);
        Assert.False(cleanup.HasPending(8));
    }

    [Fact]
    public void Closing_then_closed_failed_preserves_the_mapping()
    {
        var map = new OpenDocumentIdentityMap<object>();
        var cleanup = new DocumentCloseIdentityCleanup<object>(map.Remove);
        var document = new object();
        var id = map.GetOrAssign(document);

        cleanup.OnClosing(9, document);
        cleanup.OnClosed(9, DocumentCloseOutcome.Failed);

        Assert.True(map.TryGet(document, out var preserved));
        Assert.Equal(id, preserved);
        Assert.False(cleanup.HasPending(9));
    }

    [Fact]
    public void Unmatched_closed_event_is_harmless()
    {
        var map = new OpenDocumentIdentityMap<object>();
        var cleanup = new DocumentCloseIdentityCleanup<object>(map.Remove);
        var document = new object();
        var id = map.GetOrAssign(document);

        cleanup.OnClosed(99, DocumentCloseOutcome.Succeeded);

        Assert.True(map.TryGet(document, out var preserved));
        Assert.Equal(id, preserved);
        Assert.False(cleanup.HasPending(99));
    }
}
