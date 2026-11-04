using BimIt.Revit.Addin.Identity;
using Xunit;

namespace BimIt.Revit.Addin.Tests;

public sealed class DocumentCloseIdentityCleanupTests
{
    [Fact]
    public void Closing_then_closed_succeeded_forgets_the_mapping()
    {
        var map = new OpenDocumentIdentityMap<object>();
        var cleanup = Cleanup(map);
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
        var cleanup = Cleanup(map);
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
        var cleanup = Cleanup(map);
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
        var cleanup = Cleanup(map);
        var document = new object();
        var id = map.GetOrAssign(document);

        cleanup.OnClosed(99, DocumentCloseOutcome.Succeeded);

        Assert.True(map.TryGet(document, out var preserved));
        Assert.Equal(id, preserved);
        Assert.False(cleanup.HasPending(99));
    }

    // Live Revit 2026.5: by DocumentClosed the Document is invalid and GetHashCode throws, so every
    // Document-keyed lookup must happen during DocumentClosing.
    [Fact]
    public void Successful_close_does_not_touch_the_document_after_it_is_invalidated()
    {
        var map = new OpenDocumentIdentityMap<RevitLikeDocument>();
        var forgotten = new List<string>();
        var cleanup = Cleanup(map, forgotten);
        var document = new RevitLikeDocument(1);
        var id = map.GetOrAssign(document);

        cleanup.OnClosing(20, document);
        document.Invalidate();
        cleanup.OnClosed(20, DocumentCloseOutcome.Succeeded);

        Assert.Equal(new[] { id }, forgotten);
        Assert.False(cleanup.HasPending(20));
        Assert.False(map.TryGet(new RevitLikeDocument(1), out _));
    }

    [Fact]
    public void Successful_close_forgets_exactly_once()
    {
        var map = new OpenDocumentIdentityMap<object>();
        var forgotten = new List<string>();
        var cleanup = Cleanup(map, forgotten);
        var document = new object();
        var id = map.GetOrAssign(document);

        cleanup.OnClosing(5, document);
        cleanup.OnClosing(5, document);
        cleanup.OnClosed(5, DocumentCloseOutcome.Succeeded);
        cleanup.OnClosed(5, DocumentCloseOutcome.Succeeded);

        Assert.Equal(new[] { id }, forgotten);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Cancelled_or_failed_close_restores_the_same_id_and_forgets_nothing(bool cancelled)
    {
        var map = new OpenDocumentIdentityMap<RevitLikeDocument>();
        var forgotten = new List<string>();
        var cleanup = Cleanup(map, forgotten);
        var document = new RevitLikeDocument(2);
        var id = map.GetOrAssign(document);

        cleanup.OnClosing(7, document);
        Assert.False(map.TryGet(new RevitLikeDocument(2), out _));
        cleanup.OnClosed(7, cancelled ? DocumentCloseOutcome.Cancelled : DocumentCloseOutcome.Failed);

        Assert.Empty(forgotten);
        Assert.True(map.TryGet(new RevitLikeDocument(2), out var preserved));
        Assert.Equal(id, preserved);
        Assert.False(cleanup.HasPending(7));
    }

    [Fact]
    public void Restore_keeps_the_original_id_when_a_new_one_was_assigned_during_close()
    {
        var map = new OpenDocumentIdentityMap<object>();
        var cleanup = Cleanup(map);
        var document = new object();
        var id = map.GetOrAssign(document);

        cleanup.OnClosing(3, document);
        var interim = map.GetOrAssign(document);
        cleanup.OnClosed(3, DocumentCloseOutcome.Cancelled);

        Assert.NotEqual(id, interim);
        Assert.True(map.TryGet(document, out var preserved));
        Assert.Equal(id, preserved);
    }

    [Fact]
    public void Mismatched_or_missing_correlation_does_not_forget_another_document()
    {
        var map = new OpenDocumentIdentityMap<object>();
        var forgotten = new List<string>();
        var cleanup = Cleanup(map, forgotten);
        var closing = new object();
        var other = new object();
        var closingId = map.GetOrAssign(closing);
        var otherId = map.GetOrAssign(other);

        cleanup.OnClosed(40, DocumentCloseOutcome.Succeeded);
        cleanup.OnClosing(41, closing);
        cleanup.OnClosed(42, DocumentCloseOutcome.Succeeded);

        Assert.Empty(forgotten);
        Assert.True(map.TryGet(other, out var stillOther));
        Assert.Equal(otherId, stillOther);
        Assert.True(cleanup.HasPending(41));

        cleanup.OnClosed(41, DocumentCloseOutcome.Succeeded);
        Assert.Equal(new[] { closingId }, forgotten);
        Assert.False(map.TryGet(closing, out _));
        Assert.True(map.TryGet(other, out stillOther));
        Assert.Equal(otherId, stillOther);
    }

    [Fact]
    public void Closing_a_document_without_state_is_harmless()
    {
        var map = new OpenDocumentIdentityMap<object>();
        var forgotten = new List<string>();
        var cleanup = Cleanup(map, forgotten);
        var unknown = new object();

        cleanup.OnClosing(1, unknown);
        cleanup.OnClosed(1, DocumentCloseOutcome.Succeeded);
        cleanup.OnClosing(2, unknown);
        cleanup.OnClosed(2, DocumentCloseOutcome.Cancelled);

        Assert.Empty(forgotten);
        Assert.False(map.TryGet(unknown, out _));
    }

    internal static DocumentCloseIdentityCleanup<TDocument> Cleanup<TDocument>(
        OpenDocumentIdentityMap<TDocument> map,
        List<string>? forgotten = null)
        where TDocument : class
    {
        return new DocumentCloseIdentityCleanup<TDocument>(document =>
        {
            if (!map.TryDetach(document, out var id))
            {
                return DetachedDocumentState.None;
            }

            return new DetachedDocumentState(
                forget: () => forgotten?.Add(id),
                restore: () => map.Restore(document, id));
        });
    }

    // Equal by key like Revit Document wrappers; Equals and GetHashCode throw after Invalidate, as
    // Revit's do once the document is closed.
    private sealed class RevitLikeDocument(int key)
    {
        private bool _invalid;

        public void Invalidate() => _invalid = true;

        public override bool Equals(object? obj)
        {
            ThrowIfInvalid();
            return obj is RevitLikeDocument other && other.Key == key;
        }

        public override int GetHashCode()
        {
            ThrowIfInvalid();
            return key;
        }

        private int Key
        {
            get
            {
                ThrowIfInvalid();
                return key;
            }
        }

        private void ThrowIfInvalid()
        {
            if (_invalid)
            {
                throw new InvalidOperationException("The referenced object is not valid.");
            }
        }
    }
}
