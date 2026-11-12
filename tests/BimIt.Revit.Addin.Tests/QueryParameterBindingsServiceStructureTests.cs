using Xunit;

namespace BimIt.Revit.Addin.Tests;

/// <summary>
/// The CAP-0015 Addin service needs a live <c>UIApplication</c>. Its composition is checked
/// structurally; the query core it calls is executed in <see cref="QueryParameterBindingsTests"/>
/// (including "invalid and stale queries do not read the map"). Live behavior is the next gate.
/// </summary>
public sealed class QueryParameterBindingsServiceStructureTests
{
    [Fact]
    public void The_request_is_validated_before_it_is_queued_on_the_Revit_thread()
    {
        var service = Service();
        var validate = service.IndexOf("ParameterBindingQueryValidator.Validate(request)", StringComparison.Ordinal);
        var enqueue = service.IndexOf("_dispatcher.EnqueueAsync(", StringComparison.Ordinal);
        Assert.True(validate >= 0 && validate < enqueue);
        Assert.Equal(1, Count(service, "EnqueueAsync("));
    }

    [Fact]
    public void Document_guards_run_in_order_inside_the_work_item()
    {
        var execute = Slice(Service(), "private QueryParameterBindingsResult Execute(", "\n}");
        var active = execute.IndexOf("application.ActiveUIDocument?.Document", StringComparison.Ordinal);
        var noDocument = execute.IndexOf("CapabilityErrorCodes.NoActiveDocument", StringComparison.Ordinal);
        var family = execute.IndexOf("document.IsFamilyDocument", StringComparison.Ordinal);
        var unsupported = execute.IndexOf("CapabilityErrorCodes.UnsupportedDocumentKind", StringComparison.Ordinal);
        var id = execute.IndexOf("_identity.GetId(document)", StringComparison.Ordinal);
        var mismatch = execute.IndexOf("CapabilityErrorCodes.DocumentContextChanged", StringComparison.Ordinal);
        var revision = execute.IndexOf("_revisions.Current(documentId)", StringComparison.Ordinal);
        Assert.True(active >= 0 && active < noDocument && noDocument < family && family < unsupported);
        Assert.True(unsupported < id && id < mismatch && mismatch < revision);
        Assert.Contains("string.Equals(query.DocumentId, documentId, StringComparison.Ordinal)", execute, StringComparison.Ordinal);
    }

    [Fact]
    public void The_binding_map_is_read_only_after_a_successful_preparation()
    {
        var execute = Slice(Service(), "private QueryParameterBindingsResult Execute(", "\n}");
        var prepare = execute.IndexOf("ParameterBindingInventoryQuery.Prepare(", StringComparison.Ordinal);
        var read = execute.IndexOf("RevitParameterBindingReader.Read(document, _parameterRefs)", StringComparison.Ordinal);
        var complete = execute.IndexOf("ParameterBindingInventoryQuery.Complete(preparation, query, candidates, _cursors)", StringComparison.Ordinal);
        Assert.True(prepare >= 0 && prepare < read && read < complete);
        Assert.Equal(1, Count(execute, "RevitParameterBindingReader.Read("));
        Assert.Contains("_metadata.InstanceId", execute, StringComparison.Ordinal);
        Assert.Contains("hasActiveDocument: true", execute, StringComparison.Ordinal);
        Assert.Contains("isFamilyDocument: false", execute, StringComparison.Ordinal);
    }

    [Fact]
    public void The_service_reuses_the_lifetime_scoped_identity_revision_and_cursor_instances()
    {
        var service = Service();
        Assert.Contains("OpenDocumentParameterIdentityService parameterRefs", service, StringComparison.Ordinal);
        Assert.Contains("DocumentQueryRevisionService revisions", service, StringComparison.Ordinal);
        Assert.Contains("DeclarativeQueryCursorCodec cursors", service, StringComparison.Ordinal);
        foreach (var created in new[]
        {
            "new OpenDocumentParameterIdentityService",
            "new OpenDocumentIdentityService",
            "new ParameterIdentityMap",
            "new DocumentQueryRevisionService",
            "new DeclarativeQueryCursorCodec"
        })
        {
            Assert.DoesNotContain(created, service, StringComparison.Ordinal);
        }

        var adapters = Source("Lifecycle", "RevitLifecycleAdapters.cs");
        Assert.Contains(
            "new RevitQueryParameterBindingsService(_dispatcher, metadata, _identity, _parameterRefs, _revisions, _cursors, new RevitBuiltInCategoryCatalog())",
            adapters,
            StringComparison.Ordinal);
        Assert.Contains(
            "new RevitQueryFamilyTypesService(_dispatcher, metadata, _identity, _revisions, _cursors, new RevitBuiltInCategoryCatalog())",
            adapters,
            StringComparison.Ordinal);
        Assert.Equal(1, Count(adapters, "private readonly DocumentQueryRevisionService _revisions = new();"));
        Assert.Equal(1, Count(adapters, "private readonly DeclarativeQueryCursorCodec _cursors = new();"));
        Assert.Equal(0, Count(adapters, "new DocumentQueryRevisionService("));
        Assert.Equal(0, Count(adapters, "new DeclarativeQueryCursorCodec("));
        Assert.Equal(2, Count(adapters, "_revisions, _cursors, new RevitBuiltInCategoryCatalog()"));

        var coordinator = Source("Lifecycle", "AddinLifecycleCoordinator.cs");
        Assert.Contains("dispatcher.CreateQueryParameterBindings(metadata)", coordinator, StringComparison.Ordinal);
        Assert.Contains("queryFamilyTypes, queryParameterBindings, readOperations, startup.Token)", coordinator, StringComparison.Ordinal);
        Assert.Contains("queryFamilyTypes, queryParameterBindings, readOperations, cancellationToken)", adapters, StringComparison.Ordinal);
    }

    [Fact]
    public void Failures_are_compact_and_no_transaction_is_opened()
    {
        var service = Service();
        var failure = Slice(service, "catch (BridgeException)", "private QueryParameterBindingsResult Execute(");
        Assert.Contains("catch (OperationCanceledException)", service, StringComparison.Ordinal);
        Assert.Contains("throw ParameterBindingInventoryQuery.Unreadable();", failure, StringComparison.Ordinal);
        Assert.Contains(
            "CapabilityErrorCodes.ExecutionFailed, \"The parameter binding query could not be read.\"",
            Source("Query", "ParameterBindingQueryEngine.cs"),
            StringComparison.Ordinal);

        foreach (var forbidden in new[] { "Transaction", "SubTransaction", "TransactionGroup", "FilteredElementCollector", "GetLinkDocument", "RevitLinkInstance" })
        {
            Assert.DoesNotContain(forbidden, service, StringComparison.Ordinal);
        }
    }

    private static string Service() => Source("Capabilities", "RevitQueryParameterBindingsService.cs");

    private static string Source(string folder, string file)
    {
        return File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "BimIt.Revit.Addin", folder, file)).Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static string Slice(string text, string start, string end)
    {
        var from = text.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, "missing: " + start);
        var to = text.IndexOf(end, from + start.Length, StringComparison.Ordinal);
        Assert.True(to > from, "missing: " + end);
        return text[from..to];
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

    private static string FindRepoRoot()
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

        throw new InvalidOperationException("Could not locate BimIt.Revit.sln from the test output directory.");
    }
}
