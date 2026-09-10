using RevitMCP.Addin.Identity;
using Xunit;

namespace RevitMCP.Addin.Tests;

public sealed class OpenDocumentIdentityMapTests
{
    [Fact]
    public void Same_identity_key_returns_the_same_id()
    {
        var map = new OpenDocumentIdentityMap<object>();
        var key = new object();

        var first = map.GetOrAssign(key);
        var second = map.GetOrAssign(key);

        Assert.Equal(first, second);
        Assert.False(string.IsNullOrWhiteSpace(first));
    }

    [Fact]
    public void Different_identity_keys_return_different_ids()
    {
        var map = new OpenDocumentIdentityMap<object>();

        var first = map.GetOrAssign(new object());
        var second = map.GetOrAssign(new object());

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Assigned_id_is_opaque_and_not_based_on_caller_visible_names_or_paths()
    {
        var sequence = 0;
        var map = new OpenDocumentIdentityMap<NamedKey>(() => $"opaque-{Interlocked.Increment(ref sequence)}");
        var key = new NamedKey("Hospital-MEP.rvt", @"C:\Models\Hospital-MEP.rvt");

        var id = map.GetOrAssign(key);

        Assert.Equal("opaque-1", id);
        Assert.DoesNotContain(key.Title, id, StringComparison.Ordinal);
        Assert.DoesNotContain(key.Path, id, StringComparison.Ordinal);
        Assert.DoesNotContain("Hospital", id, StringComparison.OrdinalIgnoreCase);
        Assert.False(map.TryGet(new NamedKey(key.Title, key.Path), out _));
    }

    [Fact]
    public void Distinct_wrappers_with_the_same_logical_equality_share_one_id()
    {
        var map = new OpenDocumentIdentityMap<LogicalDocumentKey>();
        var firstWrapper = new LogicalDocumentKey(7);
        var secondWrapper = new LogicalDocumentKey(7);

        Assert.False(ReferenceEquals(firstWrapper, secondWrapper));
        Assert.Equal(firstWrapper, secondWrapper);

        var first = map.GetOrAssign(firstWrapper);
        var second = map.GetOrAssign(secondWrapper);

        Assert.Equal(first, second);
        Assert.True(map.TryGet(secondWrapper, out var lookedUp));
        Assert.Equal(first, lookedUp);
    }

    [Fact]
    public void Removal_through_an_equal_wrapper_forgets_the_mapping()
    {
        var map = new OpenDocumentIdentityMap<LogicalDocumentKey>();
        var original = map.GetOrAssign(new LogicalDocumentKey(3));

        Assert.True(map.Remove(new LogicalDocumentKey(3)));
        Assert.False(map.TryGet(new LogicalDocumentKey(3), out _));

        var replacement = map.GetOrAssign(new LogicalDocumentKey(3));
        Assert.NotEqual(original, replacement);
    }

    [Fact]
    public void Different_logical_documents_receive_different_ids()
    {
        var map = new OpenDocumentIdentityMap<LogicalDocumentKey>();

        var first = map.GetOrAssign(new LogicalDocumentKey(1));
        var second = map.GetOrAssign(new LogicalDocumentKey(2));

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Obsolete_keys_can_be_released_and_are_not_retained()
    {
        var map = new OpenDocumentIdentityMap<object>();
        var key = new object();
        var original = map.GetOrAssign(key);

        Assert.True(map.Remove(key));
        Assert.False(map.TryGet(key, out _));

        var replacement = map.GetOrAssign(key);
        Assert.False(string.IsNullOrWhiteSpace(replacement));
        Assert.NotEqual(original, replacement);
    }

    [Fact]
    public void Identity_sources_do_not_read_document_paths_or_create_transactions()
    {
        var directory = Path.Combine(FindRepoRoot(), "src", "RevitMCP.Addin", "Identity");
        var sources = Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories);
        Assert.NotEmpty(sources);

        foreach (var source in sources)
        {
            var text = File.ReadAllText(source);
            Assert.DoesNotContain("new Transaction", text, StringComparison.Ordinal);
            Assert.DoesNotContain("new SubTransaction", text, StringComparison.Ordinal);
            Assert.DoesNotContain("new TransactionGroup", text, StringComparison.Ordinal);
            Assert.DoesNotContain("PathName", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Title", text, StringComparison.Ordinal);
            Assert.DoesNotContain("GetCloudModelPath", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Username", text, StringComparison.Ordinal);
            Assert.DoesNotContain("ProcessId", text, StringComparison.Ordinal);
            Assert.DoesNotContain("ExtensibleStorage", text, StringComparison.Ordinal);
            Assert.DoesNotContain("ConditionalWeakTable", text, StringComparison.Ordinal);
        }
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

    private sealed class LogicalDocumentKey
    {
        public LogicalDocumentKey(int sessionToken)
        {
            SessionToken = sessionToken;
        }

        public int SessionToken { get; }

        public override bool Equals(object? obj) =>
            obj is LogicalDocumentKey other && SessionToken == other.SessionToken;

        public override int GetHashCode() => SessionToken;
    }

    private sealed class NamedKey
    {
        public NamedKey(string title, string path)
        {
            Title = title;
            Path = path;
        }

        public string Title { get; }

        public string Path { get; }
    }
}
