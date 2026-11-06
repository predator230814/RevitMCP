using BimIt.Revit.Addin.Query;
using Xunit;

namespace BimIt.Revit.Addin.Tests;

/// <summary>
/// The CAP-0015 Revit reader needs a live document. Its source is checked structurally here;
/// actual model facts and Parameter-versus-Definition parity belong to the live gate.
/// </summary>
public sealed class ParameterBindingReaderStructureTests
{
    [Fact]
    public void The_reader_traverses_the_whole_binding_map_with_its_forward_iterator()
    {
        var reader = Reader();
        Assert.Contains("document.ParameterBindings", reader, StringComparison.Ordinal);
        Assert.Contains("bindings.ForwardIterator()", reader, StringComparison.Ordinal);
        Assert.Contains("iterator.Reset()", reader, StringComparison.Ordinal);
        Assert.Contains("while (iterator.MoveNext())", reader, StringComparison.Ordinal);
        Assert.Contains("var expected = bindings.Size;", reader, StringComparison.Ordinal);
        Assert.Contains("if (rows.Count != expected)", reader, StringComparison.Ordinal);
        Assert.DoesNotContain("break;\n        }\n\n        // The whole map", reader, StringComparison.Ordinal);
        Assert.DoesNotContain("continue;", reader, StringComparison.Ordinal);
    }

    [Fact]
    public void Keys_must_be_internal_definitions_and_values_instance_or_type_bindings()
    {
        var loop = Slice(Reader(), "while (iterator.MoveNext())", "// The whole map");
        Assert.Contains("if (iterator.Key is not InternalDefinition definition)", loop, StringComparison.Ordinal);
        Assert.Contains("case InstanceBinding instanceBinding:", loop, StringComparison.Ordinal);
        Assert.Contains("case TypeBinding typeBinding:", loop, StringComparison.Ordinal);
        Assert.Contains("QueryParameterBindingKind.Instance", loop, StringComparison.Ordinal);
        Assert.Contains("QueryParameterBindingKind.Type", loop, StringComparison.Ordinal);
        Assert.Contains("GetElementParameterSource.Instance", loop, StringComparison.Ordinal);
        Assert.Contains("GetElementParameterSource.Type", loop, StringComparison.Ordinal);
        Assert.Equal(2, Count(loop, "throw ParameterBindingInventoryQuery.Unreadable();"));

        var engine = Source("Query", "ParameterBindingQueryEngine.cs");
        Assert.Contains("CapabilityErrorCodes.ExecutionFailed, \"The parameter binding query could not be read.\"", engine, StringComparison.Ordinal);
    }

    [Fact]
    public void Identity_data_type_and_parameter_ref_reuse_the_existing_CAP_0004_services()
    {
        var reader = Reader();
        Assert.Contains("RevitParameterIdentity.Classify(definition, document)", reader, StringComparison.Ordinal);
        Assert.Contains("RevitParameterIdentity.ClassifyDataType(definition)", reader, StringComparison.Ordinal);
        Assert.Contains("OpenDocumentParameterIdentityService parameterRefs", reader, StringComparison.Ordinal);
        Assert.Contains("parameterRefs.GetRef(document, source, identity)", reader, StringComparison.Ordinal);
        Assert.DoesNotContain("new ParameterIdentityMap", reader, StringComparison.Ordinal);
        Assert.DoesNotContain("Guid.NewGuid", reader, StringComparison.Ordinal);
        Assert.DoesNotContain("ParameterUtils", reader, StringComparison.Ordinal);
        Assert.DoesNotContain("GuidValue", reader, StringComparison.Ordinal);
        Assert.Contains("definition.Name ?? string.Empty", reader, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_bound_category_is_read_with_the_shared_token_helper()
    {
        var categories = Slice(Reader(), "private static List<ParameterBindingCategoryCandidate> ReadCategories", "private static string ReadName");
        Assert.Contains("var set = binding.Categories;", categories, StringComparison.Ordinal);
        Assert.Contains("foreach (var item in set)", categories, StringComparison.Ordinal);
        Assert.Contains("RevitCategoryTokens.Read(category)", categories, StringComparison.Ordinal);
        Assert.Contains("CategoryKey(category, entry, position)", categories, StringComparison.Ordinal);
        Assert.DoesNotContain("continue;", categories, StringComparison.Ordinal);

        var key = Slice(Reader(), "private static string CategoryKey", "\n}");
        Assert.Contains("\"id:\" + id.Value.ToString(CultureInfo.InvariantCulture)", key, StringComparison.Ordinal);
        Assert.Contains("\"entry:\"", key, StringComparison.Ordinal);

        // CAP-0014 and CAP-0015 share one category-token mapping.
        var familyService = Source("Capabilities", "RevitQueryFamilyTypesService.cs");
        Assert.Contains("RevitCategoryTokens.Read(symbol.Category)", familyService, StringComparison.Ordinal);
        Assert.DoesNotContain("Enum.GetName(builtIn)", familyService, StringComparison.Ordinal);
        var catalog = Source("Query", "RevitBuiltInCategoryCatalog.cs");
        Assert.Equal(1, Count(catalog, "internal static class RevitCategoryTokens"));
        Assert.Contains("builtIn == BuiltInCategory.INVALID || !Enum.IsDefined(builtIn)", catalog, StringComparison.Ordinal);
        Assert.Equal("unknown", DeclarativeQueryFilterRules.UnknownCategoryToken);
        Assert.Equal("unknown", FamilyPlacementTokens.Unknown);
    }

    [Fact]
    public void The_reader_is_read_only_and_host_only()
    {
        var reader = Reader();
        foreach (var forbidden in new[]
        {
            "Transaction",
            "SubTransaction",
            "TransactionGroup",
            "FilteredElementCollector",
            "OfClass(typeof(ParameterElement))",
            "SharedParameterElement",
            "RevitLinkInstance",
            "GetLinkDocument",
            "SharedParametersFilename",
            "OpenSharedParameterFile",
            "File.",
            "Directory.",
            "HttpClient",
            ".Insert(",
            ".ReInsert(",
            ".Remove(",
            ".Erase("
        })
        {
            Assert.DoesNotContain(forbidden, reader, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_reader_is_reached_only_through_the_Addin_service_and_not_from_the_Server()
    {
        var root = FindRepoRoot();
        var addin = Directory.GetFiles(Path.Combine(root, "src", "BimIt.Revit.Addin"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                && !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .ToArray();
        var users = addin
            .Where(path => File.ReadAllText(path).Contains("RevitParameterBindingReader", StringComparison.Ordinal))
            .Select(path => Path.GetFileName(path))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(["RevitParameterBindingReader.cs", "RevitQueryParameterBindingsService.cs"], users);

        // The Server exposes the MCP tool through the Bridge only. Neither it nor the Assistant touches the reader
        // or the Revit binding map, and the Assistant discovers the read generically instead of naming the tool.
        foreach (var project in new[] { "BimIt.Revit.Server", "BimIt.Revit.Assistant", "BimIt.Revit.Assistant.Ipc" })
        {
            var directory = Path.Combine(root, "src", project);
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (var path in Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories)
                .Where(path => !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                    && !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
            {
                var text = File.ReadAllText(path);
                Assert.DoesNotContain("RevitParameterBindingReader", text, StringComparison.Ordinal);
                Assert.DoesNotContain("ParameterBindings.ForwardIterator", text, StringComparison.Ordinal);
                if (project != "BimIt.Revit.Server")
                {
                    Assert.DoesNotContain("QueryParameterBindings", text, StringComparison.Ordinal);
                    Assert.DoesNotContain("query_parameter_bindings", text, StringComparison.Ordinal);
                }
            }
        }
    }

    private static string Reader() => Source("Query", "RevitParameterBindingReader.cs");

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
