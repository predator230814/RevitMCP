using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;
using BimIt.Revit.QueryRuntimeSpike;
using Xunit;

namespace BimIt.Revit.QueryRuntimeSpike.Tests;

public sealed class QueryRuntimeSpikeTests
{
    private static readonly DocumentReadiness ReadOnlyProject = new(true, false, false);

    private static string RevitApiPath => Path.Combine(AppContext.BaseDirectory, "RevitAPI.dll");

    [Fact]
    public void Collector_query_compiles_against_the_revit_api_reference_and_does_not_load_outside_revit()
    {
        var compiler = QueryCompiler.ForRevitApi(RevitApiPath);
        var compiled = compiler.Compile(QueryRuntimeSamples.WallCollector);
        Assert.True(compiled.Success, Format(compiled.Diagnostics));
        Assert.NotNull(compiled.Assembly);
        Assert.True(HasRevitApiReference(compiled.Assembly));
        Assert.Equal("RevitAPI", compiler.ApiIdentity.Name);
        Assert.Equal("26.4.10.0", compiler.ApiIdentity.Version);

        var level = compiler.Compile(QueryRuntimeSamples.LevelProjection);
        Assert.True(level.Success, Format(level.Diagnostics));

        var evidence = QueryExecutor.Execute(
            compiled,
            ReadOnlyProject,
            null,
            (Func<object?, string>)(_ => "ref"),
            new QueryBudget(10));
        Assert.False(evidence.Result.Success);
        Assert.Contains(evidence.Result.Diagnostics, diagnostic => diagnostic.Code == "BIMQ140");
    }

    [Fact]
    public void Host_query_returns_element_ref_and_a_structured_list()
    {
        var evidence = Run("""
            return new[]
            {
                new { id = elementRef(null), name = "Level 1" },
                new { id = elementRef(null), name = "Level 2" }
            };
            """);

        Assert.True(evidence.Result.Success, Format(evidence.Result.Diagnostics));
        Assert.Equal(
            """[{"id":"none","name":"Level 1"},{"id":"none","name":"Level 2"}]""",
            evidence.Result.Json);
    }

    [Fact]
    public void Syntax_error_is_a_small_deterministic_diagnostic()
    {
        var compiled = QueryCompiler.ForRevitApi(RevitApiPath).Compile("return new {;");
        Assert.False(compiled.Success);
        Assert.Null(compiled.Assembly);
        Assert.InRange(compiled.Diagnostics.Count, 1, QueryPolicy.MaxDiagnostics);
        Assert.Contains(compiled.Diagnostics, item => item.Code.StartsWith("CS", StringComparison.Ordinal));
        Assert.All(compiled.Diagnostics, item =>
        {
            Assert.Equal("error", item.Severity);
            Assert.False(string.IsNullOrWhiteSpace(item.Message));
        });
    }

    [Fact]
    public void Unknown_revit_member_returns_a_compiler_diagnostic()
    {
        var compiled = QueryCompiler.ForRevitApi(RevitApiPath).Compile("return doc.NotARealMember;");
        Assert.False(compiled.Success);
        Assert.Null(compiled.Assembly);
        var diagnostic = Assert.Single(compiled.Diagnostics, item => item.Code == "CS1061");
        Assert.Equal(1, diagnostic.Line);
        Assert.Contains("NotARealMember", diagnostic.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("return System.IO.File.Exists(\"a\");", "System.IO")]
    [InlineData("return new System.Net.Http.HttpClient();", "System.Net")]
    [InlineData("return System.Diagnostics.Process.GetCurrentProcess();", "System.Diagnostics")]
    [InlineData("return System.Reflection.Assembly.GetExecutingAssembly();", "System.Reflection")]
    [InlineData("return new Transaction(doc, \"spike\");", "Transaction")]
    [InlineData("return new SubTransaction(doc);", "SubTransaction")]
    [InlineData("return new TransactionGroup(doc, \"spike\");", "TransactionGroup")]
    [InlineData("return new Autodesk.Revit.UI.TaskDialog(\"spike\");", "Autodesk.Revit.UI")]
    [InlineData("return doc.Application.VersionNumber;", "Autodesk.Revit.ApplicationServices")]
    [InlineData("doc.Save(); return 0;", "Save")]
    [InlineData("doc.Close(); return 0;", "Close")]
    [InlineData("return System.Threading.Tasks.Task.Run(() => 1);", "System.Threading")]
    [InlineData("unsafe { return 1; }", "unsafe")]
    [InlineData("[System.Runtime.InteropServices.DllImport(\"kernel32\")] static void Native(); return 0;", "System.Runtime.InteropServices")]
    public void Forbidden_capability_is_rejected_before_execution(string source, string expected)
    {
        var compiled = QueryCompiler.ForRevitApi(RevitApiPath).Compile(source);
        Assert.False(compiled.Success);
        Assert.Null(compiled.Assembly);
        Assert.Contains(compiled.Diagnostics, diagnostic =>
            diagnostic.Code == "BIMQ110"
            && diagnostic.Message.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void Loop_instrumentation_stops_a_tight_loop_and_allows_a_bounded_one()
    {
        var timeout = Run("""
            var n = 0;
            while (n < 100) n++;
            return n;
            """, budget: 5);
        Assert.False(timeout.Result.Success);
        Assert.Contains(timeout.Result.Diagnostics, diagnostic => diagnostic.Code == "BIMQ120");

        var completed = Run("""
            var n = 0;
            for (var i = 0; i < 2; i++) n++;
            foreach (var value in new[] { 1 }) n++;
            var k = 0;
            while (k < 1) { k++; n++; }
            do { n++; } while (n < 1);
            return n;
            """, budget: 20);
        Assert.True(completed.Result.Success, Format(completed.Result.Diagnostics));
        Assert.Equal("5", completed.Result.Json);
        Assert.Equal(
            "A blocking Revit API call cannot be safely interrupted. The budget check runs when an instrumented loop body is entered.",
            QueryBudget.BlockingCallLimitation);
    }

    [Fact]
    public void Source_and_result_size_bounds_fail_closed()
    {
        var source = QueryCompiler.ForHostProbe().Compile(new string('a', QueryPolicy.MaxSourceCharacters + 1));
        Assert.False(source.Success);
        Assert.Null(source.Assembly);
        Assert.Contains(source.Diagnostics, diagnostic => diagnostic.Code == "BIMQ100");

        var result = Run("return new string('x', 20000);");
        Assert.False(result.Result.Success);
        Assert.Contains(result.Result.Diagnostics, diagnostic => diagnostic.Code == "BIMQ101");
    }

    [Fact]
    public void Modifiable_or_family_document_is_not_executed()
    {
        var compiled = QueryCompiler.ForHostProbe().Compile("return elementRef(null);");
        var called = false;
        string Mark(object? _)
        {
            called = true;
            return "ran";
        }

        var modifiable = QueryExecutor.Execute(compiled, new DocumentReadiness(true, false, true), null, (Func<object?, string>)Mark, new QueryBudget(10));
        var family = QueryExecutor.Execute(compiled, new DocumentReadiness(true, true, false), null, (Func<object?, string>)Mark, new QueryBudget(10));
        var missing = QueryExecutor.Execute(compiled, new DocumentReadiness(false, false, false), null, (Func<object?, string>)Mark, new QueryBudget(10));

        Assert.False(called);
        Assert.Contains(modifiable.Result.Diagnostics, diagnostic => diagnostic.Message == "document_modifiable");
        Assert.Contains(family.Result.Diagnostics, diagnostic => diagnostic.Message == "project_document_required");
        Assert.Contains(missing.Result.Diagnostics, diagnostic => diagnostic.Message == "no_active_document");
    }

    [Fact]
    public void Collectible_context_is_collectible_after_execution()
    {
        var evidence = Run("return 1;");
        Assert.Equal("1", evidence.Result.Json);
        Assert.NotNull(evidence.Context);
        for (var attempt = 0; attempt < 10 && evidence.Context.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        Assert.False(evidence.Context.IsAlive);
    }

    [Fact]
    public void Anonymous_result_does_not_pin_the_collectible_context()
    {
        var evidence = Run("return new { count = 1, name = elementRef(null) };");
        Assert.Equal("""{"count":1,"name":"none"}""", evidence.Result.Json);
        Assert.True(QueryExecSequence.TryRelease(evidence.Context));
    }

    [Fact]
    public void Same_source_has_a_stable_cache_identity()
    {
        var compiler = QueryCompiler.ForRevitApi(RevitApiPath);
        var cache = new QueryCompilationCache();
        var first = cache.GetOrCompile(compiler, "return 1;");
        var second = cache.GetOrCompile(compiler, "return 1;");
        Assert.True(first.Success, Format(first.Diagnostics));
        Assert.Equal(first.CacheKey, second.CacheKey);
        Assert.Equal(64, first.CacheKey.Length);
        Assert.Equal(1, cache.Misses);
        Assert.NotEqual(first.CacheKey, compiler.Compile("return 2;").CacheKey);
        Assert.NotEqual(
            first.CacheKey,
            QueryCacheKey.Create("return 1;", QueryPolicy.ContractRevitDocument, compiler.ApiIdentity, QueryPolicy.Version + 1));
        var otherApi = compiler.ApiIdentity with { Length = compiler.ApiIdentity.Length + 1 };
        Assert.NotEqual(
            first.CacheKey,
            QueryCacheKey.Create("return 1;", QueryPolicy.ContractRevitDocument, otherApi, QueryPolicy.Version));
    }

    [Fact]
    public void Symbol_index_describes_non_element_types_methods_overloads_and_enums()
    {
        using var revit = OpenRevitApi(out var assembly);
        var index = new RevitApiSymbolIndex();

        var point = index.Describe(assembly, "XYZ");
        Assert.NotNull(point);
        Assert.Equal("Autodesk.Revit.DB.XYZ", point.FullName);
        Assert.Equal("class", point.Kind);
        Assert.DoesNotContain("Element", point.BaseType ?? "", StringComparison.Ordinal);
        Assert.Contains(point.Properties, property => property.Name == "X");
        Assert.Contains(point.Properties, property => property.Name == "Y");
        Assert.Contains(point.Properties, property => property.Name == "Z");

        var collector = index.Describe(assembly, "FilteredElementCollector");
        Assert.NotNull(collector);
        Assert.Equal("class", collector.Kind);
        Assert.Equal("System.Object", collector.BaseType);
        Assert.Contains(collector.Methods, method => method.Name == "OfClass");

        var line = index.Describe(assembly, "Line");
        Assert.NotNull(line);
        Assert.Contains(line.Factories, factory => factory.Contains("CreateBound", StringComparison.Ordinal));
        Assert.Contains(line.Methods, method => method.Name == "CreateBound" && method.IsStatic);

        var transform = index.Describe(assembly, "ElementTransformUtils");
        Assert.NotNull(transform);
        var copy = Assert.Single(transform.Methods, method => method.Name == "CopyElements");
        Assert.True(copy.Overloads.Count >= 2);

        var category = index.Describe(assembly, "BuiltInCategory");
        Assert.NotNull(category);
        Assert.Equal("enum", category.Kind);
        Assert.Contains("OST_Walls", category.EnumValues);
        Assert.True(category.EnumValuesTruncated);
        Assert.True(category.EnumValueCount > RevitApiSymbolIndex.MaxEnumValues);
        Assert.True(category.EnumValues.Count <= RevitApiSymbolIndex.MaxEnumValues);

        var obsoleteType = ExportedTypes(assembly)
            .Where(type => type.IsPublic && !type.IsNested && type.Namespace == "Autodesk.Revit.DB" && IsObsolete(type))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .First();
        var describedObsolete = index.Describe(assembly, obsoleteType.FullName!);
        Assert.NotNull(describedObsolete);
        Assert.Equal(obsoleteType.FullName, describedObsolete.FullName);
        Assert.True(describedObsolete.Obsolete);
    }

    [Fact]
    public void Production_addin_server_and_protocol_are_unchanged()
    {
        var root = FindRepoRoot();
        foreach (var project in Directory.GetFiles(Path.Combine(root, "src"), "*.csproj", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(project);
            Assert.DoesNotContain("QueryRuntimeSpike", text, StringComparison.Ordinal);
            if (!project.EndsWith("BimIt.Revit.CompilerHost.csproj", StringComparison.OrdinalIgnoreCase))
            {
                Assert.DoesNotContain("PackageReference Include=\"Microsoft.CodeAnalysis", text, StringComparison.Ordinal);
            }
        }

        var protocol = File.ReadAllText(Path.Combine(root, "src", "BimIt.Revit.Contracts", "BridgeProtocol.cs"));
        Assert.Contains("public const int QueryElementPredicatesVersion = 25;", protocol, StringComparison.Ordinal);
        Assert.Contains("public const int CurrentVersion = RunCSharpQueryVersion;", protocol, StringComparison.Ordinal);

        var server = File.ReadAllText(Path.Combine(root, "src", "BimIt.Revit.Server", "ServerHost.cs"));
        Assert.Equal(22, Regex.Matches(server, @"\.With\w+Tool\(\)").Count);

        var live = File.ReadAllText(Path.Combine(
            root,
            "spikes",
            "BimIt.Revit.QueryRuntimeSpike.Live",
            "QueryRuntimeLiveCommand.cs"));
        Assert.DoesNotContain("new Transaction", live, StringComparison.Ordinal);
        Assert.Contains("RevitExecutionDispatcher.Create", live, StringComparison.Ordinal);
        Assert.Contains("EnqueueAsync", live, StringComparison.Ordinal);
        Assert.DoesNotContain(".Wait(", live, StringComparison.Ordinal);
        Assert.DoesNotContain(".Result", live, StringComparison.Ordinal);
        Assert.DoesNotContain("GetAwaiter", live, StringComparison.Ordinal);
        Assert.Contains("TransactionMode.ReadOnly", live, StringComparison.Ordinal);

        var dispatcher = File.ReadAllText(Path.Combine(root, "src", "BimIt.Revit.Addin", "Execution", "RevitExecutionDispatcher.cs"));
        Assert.DoesNotContain("QueryRuntime", dispatcher, StringComparison.Ordinal);

        var liveProject = File.ReadAllText(Path.Combine(
            root,
            "spikes",
            "BimIt.Revit.QueryRuntimeSpike.Live",
            "BimIt.Revit.QueryRuntimeSpike.Live.csproj"));
        Assert.Contains("<Private>false</Private>", liveProject, StringComparison.Ordinal);
        Assert.Contains("BimIt.Revit.Addin.csproj", liveProject, StringComparison.Ordinal);
    }

    [Fact]
    public void One_loaded_assembly_is_selected_and_a_duplicate_is_rejected()
    {
        var loaded = new Assembly[] { typeof(QueryExecGate).Assembly, typeof(string).Assembly };
        Assert.Same(typeof(QueryExecGate).Assembly, LoadedAssemblySelection.SingleNamed(loaded, "BimIt.Revit.QueryRuntimeSpike"));
        Assert.Null(LoadedAssemblySelection.SingleNamed(loaded, "Missing.Assembly"));
        Assert.Null(LoadedAssemblySelection.SingleNamed(
            [typeof(QueryExecGate).Assembly, typeof(QueryExecGate).Assembly],
            "BimIt.Revit.QueryRuntimeSpike"));
        Assert.True(LoadedAssemblySelection.SharesDirectory(
            typeof(QueryExecGate).Assembly.Location,
            typeof(QueryRuntimeSpikeTests).Assembly.Location));
        Assert.False(LoadedAssemblySelection.SharesDirectory(typeof(QueryExecGate).Assembly.Location, null));
    }

    [Fact]
    public void Transaction_source_is_not_submitted_to_execution()
    {
        var gate = QueryExecGate.Prepare(QueryCompiler.ForRevitApi(RevitApiPath));
        Assert.True(gate.LevelQuery.Success, Format(gate.LevelQuery.Diagnostics));
        Assert.True(gate.TightLoop.Success, Format(gate.TightLoop.Diagnostics));
        Assert.False(gate.TransactionAttempt.Success);
        Assert.Null(gate.TransactionAttempt.Assembly);
        Assert.False(gate.TransactionExecutionEnqueued);
        Assert.Contains(gate.TransactionAttempt.Diagnostics, diagnostic => diagnostic.Code == "BIMQ110");

        var submitted = new List<QueryCompilation>();
        QueryExecSequence.Run(
            gate,
            (compilation, _) =>
            {
                submitted.Add(compilation);
                return Evidence(compilation.CacheKey);
            },
            () => false);

        Assert.Equal(4, submitted.Count);
        Assert.DoesNotContain(submitted, compilation => ReferenceEquals(compilation, gate.TransactionAttempt));
        Assert.Equal(3, submitted.Count(compilation => ReferenceEquals(compilation, gate.LevelQuery)));
        Assert.Single(submitted, compilation => ReferenceEquals(compilation, gate.TightLoop));
    }

    [Fact]
    public void Repeated_query_is_collectible_and_a_budget_failure_does_not_block_the_next_query()
    {
        var compiler = QueryCompiler.ForHostProbe();
        var query = compiler.Compile("return elementRef(null);");
        var tight = compiler.Compile(QueryRuntimeSamples.TightLoop);
        var transaction = QueryCompiler.ForRevitApi(RevitApiPath).Compile(QueryRuntimeSamples.TransactionAttempt);
        Assert.True(query.Success, Format(query.Diagnostics));
        Assert.True(tight.Success, Format(tight.Diagnostics));
        var gate = QueryExecGate.ForCompilations(query, tight, transaction);
        var modified = false;
        var steps = QueryExecSequence.Run(
            gate,
            (compilation, budget) => QueryExecutor.Execute(
                compilation,
                ReadOnlyProject,
                null,
                (Func<object?, string>)(value => value is null ? "none" : "ref"),
                new QueryBudget(budget)),
            () => modified);

        Assert.Equal(4, steps.Count);
        Assert.Equal(["level", "level-repeat", "tight-loop", "level-after-budget"], steps.Select(step => step.Name).ToArray());
        Assert.True(steps[0].Success, steps[0].Code);
        Assert.Equal("\"none\"", steps[0].Json);
        Assert.Equal(steps[0].Json, steps[1].Json);
        Assert.Equal(steps[0].CacheKey, steps[1].CacheKey);
        Assert.Equal(steps[0].CacheKey, steps[3].CacheKey);
        Assert.True(steps[0].ContextCollected);
        Assert.True(steps[1].ContextCollected);
        Assert.False(steps[2].Success);
        Assert.Equal("BIMQ120", steps[2].Code);
        Assert.True(steps[2].ContextCollected);
        Assert.True(steps[3].Success, steps[3].Code);
        Assert.True(steps[3].ContextCollected);
        Assert.All(steps, step =>
        {
            Assert.False(step.ModifiedBefore);
            Assert.False(step.ModifiedAfter);
        });
        Assert.False(modified);
    }

    private static QueryExecutionEvidence Evidence(string cacheKey)
    {
        return new QueryExecutionEvidence
        {
            Result = new QueryRunResult
            {
                Success = true,
                Json = "{}",
                CacheKey = cacheKey,
                Diagnostics = []
            },
            Context = new WeakReference(new object())
        };
    }

    private static QueryExecutionEvidence Run(string source, int budget = 100)
    {
        var compiled = QueryCompiler.ForHostProbe().Compile(source);
        Assert.True(compiled.Success, Format(compiled.Diagnostics));
        return QueryExecutor.Execute(
            compiled,
            ReadOnlyProject,
            null,
            (Func<object?, string>)(value => value is null ? "none" : "ref"),
            new QueryBudget(budget));
    }

    private static bool HasRevitApiReference(byte[] assembly)
    {
        using var stream = new MemoryStream(assembly, writable: false);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        foreach (var handle in reader.AssemblyReferences)
        {
            var reference = reader.GetAssemblyReference(handle);
            if (reader.GetString(reference.Name) == "RevitAPI" && reference.Version == new Version(26, 4, 10, 0))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<Type> ExportedTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetExportedTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.Where(type => type is not null)!;
        }
    }

    private static bool IsObsolete(MemberInfo member)
    {
        return member.GetCustomAttributesData().Any(attribute => attribute.AttributeType.Name == "ObsoleteAttribute");
    }

    private static MetadataLoadContext OpenRevitApi(out Assembly assembly)
    {
        var runtime = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var paths = Directory.GetFiles(runtime, "*.dll").Append(RevitApiPath);
        var context = new MetadataLoadContext(new PathAssemblyResolver(paths));
        assembly = context.LoadFromAssemblyPath(RevitApiPath);
        return context;
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

        throw new InvalidOperationException("Repository root was not found.");
    }

    private static string Format(IReadOnlyList<QueryDiagnostic> diagnostics)
    {
        return string.Join(" | ", diagnostics.Select(diagnostic => diagnostic.Format()));
    }
}
