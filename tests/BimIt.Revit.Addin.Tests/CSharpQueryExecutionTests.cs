using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using BimIt.Revit.Addin.CSharpQuery;
using BimIt.Revit.Addin.Execution;
using BimIt.Revit.Contracts;
using Xunit;

namespace BimIt.Revit.Addin.Tests;

public sealed class CSharpQueryExecutionTests
{
    static CSharpQueryExecutionTests()
    {
        AssemblyLoadContext.Default.Resolving += (_, name) =>
        {
            if (!string.Equals(name.Name, "RevitAPI", StringComparison.Ordinal))
            {
                return null;
            }

            var path = Path.Combine(AppContext.BaseDirectory, "revit-load", "RevitAPI.dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
    }

    [Fact]
    public void Copier_accepts_scalars_arrays_lists_dictionaries_and_nested_values()
    {
        AssertJson(null, "null");
        AssertJson(true, "true");
        AssertJson("s", "\"s\"");
        AssertJson((byte)1, "1");
        AssertJson((sbyte)-2, "-2");
        AssertJson((short)3, "3");
        AssertJson((ushort)4, "4");
        AssertJson(5, "5");
        AssertJson(6u, "6");
        AssertJson(7L, "7");
        AssertJson(8UL, "8");
        AssertJson(1.5f, "1.5");
        AssertJson(2.5d, "2.5");
        AssertJson(3.5m, "3.5");
        AssertJson('q', "\"q\"");
        AssertJson(new[] { 1, 2 }, "[1,2]");
        AssertJson(new List<int> { 1, 2 }, "[1,2]");
        AssertJson(new Dictionary<string, int> { ["b"] = 1, ["a"] = 2 }, "{\"a\":2,\"b\":1}");
        AssertJson(
            new List<Dictionary<string, int>> { new() { ["b"] = 1, ["a"] = 2 } },
            "[{\"a\":2,\"b\":1}]");
    }

    [Fact]
    public void Copier_orders_dictionary_keys_and_anonymous_properties_ordinally()
    {
        var first = CSharpQueryResultCopier.Copy(new Dictionary<string, int> { ["b"] = 1, ["a"] = 2 }, null);
        var second = CSharpQueryResultCopier.Copy(new Dictionary<string, int> { ["a"] = 2, ["b"] = 1 }, null);
        Assert.Equal(first.Json, second.Json);
        var anonymous = new { b = 1, a = "z" };
        var copied = CSharpQueryResultCopier.Copy(anonymous, anonymous.GetType().Assembly);
        Assert.Equal("{\"a\":\"z\",\"b\":1}", copied.Json);
        Assert.IsType<SortedDictionary<string, object?>>(copied.Value);
        Assert.NotSame(anonymous.GetType().Assembly, copied.Value.GetType().Assembly);
    }

    [Fact]
    public void Copier_rejects_values_outside_the_closed_graph()
    {
        AssertRejected(new Poco());
        AssertRejected(RuntimeHelpers.GetUninitializedObject(typeof(Element)));
        AssertRejected(RuntimeHelpers.GetUninitializedObject(typeof(ElementId)));
        AssertRejected(Sample.One);
        AssertRejected(Enumerable.Range(0, 2).Where(value => value >= 0));
        AssertRejected(new CustomEnumerable());
        AssertRejected(new CustomList());
        AssertRejected(new CustomDictionary());
        AssertRejected(new DerivedList());
        AssertRejected(new int[1, 1]);
        AssertRejected(float.NaN);
        AssertRejected(double.PositiveInfinity);
        var anonymous = new { name = "level" };
        AssertRejected(anonymous, typeof(string).Assembly);
    }

    [Fact]
    public void Copier_rejects_depth_node_and_serialized_bounds_without_a_partial_result()
    {
        Assert.Equal(CSharpQueryCodes.ResultTooLarge, RejectedCode(Nest(9)));
        Assert.NotNull(CSharpQueryResultCopier.Copy(Nest(8), null).Json);
        Assert.Equal(CSharpQueryCodes.ResultTooLarge, RejectedCode(Enumerable.Range(0, 1025).ToList()));
        Assert.NotNull(CSharpQueryResultCopier.Copy(Enumerable.Range(0, 1024).ToList(), null).Json);
        Assert.Equal(CSharpQueryCodes.ResultTooLarge, RejectedCode(new string('a', 15_999)));
        Assert.InRange(CSharpQueryResultCopier.Copy(new string('a', 15_998), null).Json.Length, 1, 16_000);
        Assert.NotNull(CSharpQueryResultCopier.Copy(EmptyLists(1_024), null).Json);
        Assert.Equal(CSharpQueryCodes.ResultTooLarge, RejectedCode(EmptyLists(1_025)));
        Assert.NotNull(CSharpQueryResultCopier.Copy(EmptyListArray(1_024), null).Json);
        Assert.Equal(CSharpQueryCodes.ResultTooLarge, RejectedCode(EmptyListArray(1_025)));
        Assert.NotNull(CSharpQueryResultCopier.Copy(EmptyListDictionary(1_024), null).Json);
        Assert.Equal(CSharpQueryCodes.ResultTooLarge, RejectedCode(EmptyListDictionary(1_025)));
        var acceptedProjection = EmptyProjection(1_024);
        Assert.NotNull(CSharpQueryResultCopier.Copy(acceptedProjection, acceptedProjection.GetType().Assembly).Json);
        var rejectedProjection = EmptyProjection(1_025);
        Assert.Equal(
            CSharpQueryCodes.ResultTooLarge,
            RejectedCode(rejectedProjection, rejectedProjection.GetType().Assembly));
    }

    [Fact]
    public void Kernel_executes_a_scalar_and_an_anonymous_projection_from_memory()
    {
        var scalar = Execute(Fixture("return-one.dll"));
        Assert.True(scalar.Result.Success);
        Assert.Equal(1, scalar.Result.ResultValue);
        Assert.Equal("1", scalar.Result.ResultJson);
        Assert.NotEqual("BimIt.Revit.GeneratedQuery", scalar.Result.ResultValue!.GetType().Assembly.GetName().Name);

        var anonymous = Execute(Fixture("anonymous.dll"));
        Assert.True(anonymous.Result.Success);
        Assert.Equal("{\"count\":2,\"name\":\"level\"}", anonymous.Result.ResultJson);
        Assert.IsType<SortedDictionary<string, object?>>(anonymous.Result.ResultValue);
    }

    [Fact]
    public void Kernel_rejects_an_unexpected_entry_shape()
    {
        var evidence = Execute(Fixture("wrong-shape.dll"));
        Assert.False(evidence.Result.Success);
        Assert.Equal(CSharpQueryCodes.ExecutionFailed, evidence.Result.Code);
        Assert.Null(evidence.Result.ResultJson);
    }

    [Fact]
    public void Kernel_maps_budget_and_execution_failures_and_recovers()
    {
        var budget = Execute(Fixture("budget.dll"));
        Assert.Equal(CSharpQueryCodes.BudgetExceeded, budget.Result.Code);
        Assert.Null(budget.Result.ResultJson);
        Assert.Null(budget.Result.ResultValue);

        var failed = Execute(Fixture("throw.dll"));
        Assert.Equal(CSharpQueryCodes.ExecutionFailed, failed.Result.Code);
        Assert.Null(failed.Result.ResultJson);
        Assert.DoesNotContain("SECRET_EXECUTION_TEXT", failed.Result.Code, StringComparison.Ordinal);

        var recovered = Execute(Fixture("return-one.dll"));
        Assert.True(recovered.Result.Success);
        Assert.Equal("1", recovered.Result.ResultJson);
    }

    [Fact]
    public async Task Queue_stays_usable_after_a_budget_failure()
    {
        var signal = new RecordingEventSignal();
        var queue = new RevitExecutionQueue<int>(signal);
        var first = queue.EnqueueAsync(_ => Execute(Fixture("budget.dll")).Result.Code, CancellationToken.None);
        var second = queue.EnqueueAsync(_ => Execute(Fixture("return-one.dll")).Result.Success, CancellationToken.None);
        queue.ExecutePending(0);
        Assert.Equal(CSharpQueryCodes.BudgetExceeded, await first);
        Assert.True(await second);
        Assert.Equal(RevitExecutionStatus.Idle, queue.Status);
    }

    [Fact]
    public void Generated_contexts_are_collectible_after_the_execution_method_returns()
    {
        var scalar = RunIsolated(Fixture("return-one.dll"), null);
        var projection = RunIsolated(Fixture("anonymous.dll"), null);
        var budget = RunIsolated(Fixture("budget.dll"), CSharpQueryCodes.BudgetExceeded);
        var invalid = RunIsolated(Fixture("invalid-object.dll"), CSharpQueryCodes.InvalidResult);
        var cycles = CollectUntilDead(scalar, projection, budget, invalid);
        Assert.False(scalar.IsAlive);
        Assert.False(projection.IsAlive);
        Assert.False(budget.IsAlive);
        Assert.False(invalid.IsAlive);
        Assert.InRange(cycles, 1, 10);
    }

    [Fact]
    public void Document_gate_returns_the_deterministic_reasons()
    {
        AssertDecision(new CSharpQueryDocumentObservation(false, false, false, false, false), CSharpQueryCodes.DocumentNotReady, CSharpQueryCodes.NoActiveDocument);
        AssertDecision(new CSharpQueryDocumentObservation(true, true, false, false, false), CSharpQueryCodes.DocumentNotReady, CSharpQueryCodes.ProjectDocumentRequired);
        AssertDecision(new CSharpQueryDocumentObservation(true, false, false, false, true), CSharpQueryCodes.DocumentNotReady, CSharpQueryCodes.DocumentMismatch);
        AssertDecision(new CSharpQueryDocumentObservation(true, false, true, true, true), CSharpQueryCodes.DocumentNotReady, CSharpQueryCodes.DocumentModifiable);
        AssertDecision(new CSharpQueryDocumentObservation(true, false, true, false, false), CSharpQueryCodes.ApiUnsupported, null);
        Assert.True(CSharpQueryDocumentGate.Decide(new CSharpQueryDocumentObservation(true, false, true, false, true)).Accepted);
    }

    [Fact]
    public void Production_execution_keeps_the_compiler_boundary_and_protocol()
    {
        var root = RepoRoot();
        var folder = Path.Combine(root, "src", "BimIt.Revit.Addin", "CSharpQuery");
        var combined = string.Join('\n', Directory.GetFiles(folder, "*.cs").Select(File.ReadAllText));
        Assert.Contains("EnqueueAsync", File.ReadAllText(Path.Combine(folder, "CSharpQueryExecutionService.cs")), StringComparison.Ordinal);
        Assert.DoesNotContain("Transaction", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("File.Write", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("GC.Collect", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("Microsoft.CodeAnalysis", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("QueryRuntimeSpike", combined, StringComparison.Ordinal);

        foreach (var project in Directory.GetFiles(Path.Combine(root, "src"), "*.csproj", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(project);
            Assert.DoesNotContain("QueryRuntimeSpike", text, StringComparison.Ordinal);
            if (project.EndsWith("BimIt.Revit.CompilerHost.csproj", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Assert.DoesNotContain("PackageReference Include=\"Microsoft.CodeAnalysis", text, StringComparison.Ordinal);
        }

        Assert.Equal(27, BridgeProtocol.CurrentVersion);
        var server = File.ReadAllText(Path.Combine(root, "src", "BimIt.Revit.Server", "ServerHost.cs"));
        Assert.Equal(23, Regex.Matches(server, @"\.With\w+Tool\(\)").Count);
        Assert.Contains(".WithRunCSharpQueryTool()", server, StringComparison.Ordinal);
        var assistant = string.Join('\n', Directory.GetFiles(Path.Combine(root, "src", "BimIt.Revit.Assistant"), "*.cs", SearchOption.AllDirectories).Select(File.ReadAllText));
        Assert.True(Regex.Matches(assistant, "revit_run_csharp_query").Count == 1);
        var bridge = string.Join('\n', Directory.GetFiles(Path.Combine(root, "src", "BimIt.Revit.Bridge"), "*.cs", SearchOption.AllDirectories).Select(File.ReadAllText));
        Assert.Contains("revit.run_csharp_query", bridge, StringComparison.Ordinal);
    }

    private static void AssertJson(object? value, string json)
    {
        var copied = CSharpQueryResultCopier.Copy(value, null);
        Assert.Equal(json, copied.Json);
    }

    private static void AssertRejected(object? value, Assembly? generatedAssembly = null)
    {
        Assert.Equal(CSharpQueryCodes.InvalidResult, RejectedCode(value, generatedAssembly));
    }

    private static string RejectedCode(object? value, Assembly? generatedAssembly = null)
    {
        var exception = Assert.Throws<CSharpQueryCopyException>(() => CSharpQueryResultCopier.Copy(value, generatedAssembly));
        return exception.Code;
    }

    private static List<List<int>> EmptyLists(int count)
    {
        var lists = new List<List<int>>(count);
        for (var index = 0; index < count; index++)
        {
            lists.Add([]);
        }

        return lists;
    }

    private static List<int>[] EmptyListArray(int count)
    {
        var lists = new List<int>[count];
        for (var index = 0; index < count; index++)
        {
            lists[index] = [];
        }

        return lists;
    }

    private static Dictionary<string, List<int>> EmptyListDictionary(int count)
    {
        var map = new Dictionary<string, List<int>>(count);
        for (var index = 0; index < count; index++)
        {
            map[index.ToString("D4")] = [];
        }

        return map;
    }

    private static object EmptyProjection(int count)
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("EmptyProjection" + count), AssemblyBuilderAccess.Run);
        var module = assembly.DefineDynamicModule("projection");
        var type = module.DefineType(
            "<>f__AnonymousType",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class);
        type.SetCustomAttribute(new CustomAttributeBuilder(
            typeof(CompilerGeneratedAttribute).GetConstructor(Type.EmptyTypes)!,
            []));
        var listType = typeof(List<int>);
        var listConstructor = listType.GetConstructor(Type.EmptyTypes)!;
        var fields = new FieldBuilder[count];
        for (var index = 0; index < count; index++)
        {
            var name = "P" + index.ToString("D4");
            fields[index] = type.DefineField("_" + name, listType, FieldAttributes.Private);
            var property = type.DefineProperty(name, PropertyAttributes.None, listType, null);
            var getter = type.DefineMethod(
                "get_" + name,
                MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
                listType,
                Type.EmptyTypes);
            var getterIl = getter.GetILGenerator();
            getterIl.Emit(OpCodes.Ldarg_0);
            getterIl.Emit(OpCodes.Ldfld, fields[index]);
            getterIl.Emit(OpCodes.Ret);
            property.SetGetMethod(getter);
        }

        var constructor = type.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, Type.EmptyTypes);
        var il = constructor.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes)!);
        for (var index = 0; index < count; index++)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Newobj, listConstructor);
            il.Emit(OpCodes.Stfld, fields[index]);
        }

        il.Emit(OpCodes.Ret);
        return Activator.CreateInstance(type.CreateType())!;
    }

    private static object Nest(int depth)
    {
        object value = 1;
        for (var level = 1; level < depth; level++)
        {
            value = new List<object> { value };
        }

        return value;
    }

    private static CSharpQueryExecutionEvidence Execute(byte[] assembly)
    {
        return CSharpQueryExecutionKernel.Execute(assembly, null, static _ => CSharpQueryElementRef.NullElement);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference RunIsolated(byte[] assembly, string? failureCode)
    {
        var evidence = Execute(assembly);
        if (failureCode is null)
        {
            Assert.True(evidence.Result.Success);
            Assert.NotNull(evidence.Result.ResultValue);
            Assert.NotEqual("BimIt.Revit.GeneratedQuery", evidence.Result.ResultValue.GetType().Assembly.GetName().Name);
        }
        else
        {
            Assert.Equal(failureCode, evidence.Result.Code);
            Assert.Null(evidence.Result.ResultJson);
            Assert.Null(evidence.Result.ResultValue);
        }

        return evidence.LoadContext ?? throw new InvalidOperationException("missing context");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int CollectUntilDead(params WeakReference[] references)
    {
        for (var cycle = 1; cycle <= 10; cycle++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            if (references.All(reference => !reference.IsAlive))
            {
                return cycle;
            }
        }

        return 10;
    }

    private static void AssertDecision(CSharpQueryDocumentObservation observation, string code, string? reason)
    {
        var decision = CSharpQueryDocumentGate.Decide(observation);
        Assert.Equal(code, decision.Code);
        Assert.Equal(reason, decision.Reason);
    }

    private static byte[] Fixture(string name)
    {
        var resource = typeof(CSharpQueryExecutionTests).Assembly.GetManifestResourceNames()
            .Single(item => item.EndsWith(name, StringComparison.Ordinal));
        using var stream = typeof(CSharpQueryExecutionTests).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException(name);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BimIt.Revit.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }

    private enum Sample
    {
        One
    }

    private sealed class Poco
    {
        public int Value { get; set; }
    }

    private sealed class DerivedList : List<int>;

    private sealed class CustomEnumerable : IEnumerable<int>
    {
        public IEnumerator<int> GetEnumerator() => Enumerable.Empty<int>().GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class CustomList : IList<int>
    {
        public int this[int index] { get => 0; set { } }
        public int Count => 0;
        public bool IsReadOnly => true;
        public void Add(int item) { }
        public void Clear() { }
        public bool Contains(int item) => false;
        public void CopyTo(int[] array, int arrayIndex) { }
        public IEnumerator<int> GetEnumerator() => Enumerable.Empty<int>().GetEnumerator();
        public int IndexOf(int item) => -1;
        public void Insert(int index, int item) { }
        public bool Remove(int item) => false;
        public void RemoveAt(int index) { }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class CustomDictionary : IDictionary<string, int>
    {
        public int this[string key] { get => 0; set { } }
        public ICollection<string> Keys => [];
        public ICollection<int> Values => [];
        public int Count => 0;
        public bool IsReadOnly => true;
        public void Add(string key, int value) { }
        public void Add(KeyValuePair<string, int> item) { }
        public void Clear() { }
        public bool Contains(KeyValuePair<string, int> item) => false;
        public bool ContainsKey(string key) => false;
        public void CopyTo(KeyValuePair<string, int>[] array, int arrayIndex) { }
        public IEnumerator<KeyValuePair<string, int>> GetEnumerator() => Enumerable.Empty<KeyValuePair<string, int>>().GetEnumerator();
        public bool Remove(string key) => false;
        public bool Remove(KeyValuePair<string, int> item) => false;
        public bool TryGetValue(string key, out int value) { value = 0; return false; }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
