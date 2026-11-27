using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using BimIt.Revit.Addin.ReadOperations;
using BimIt.Revit.Bridge;
using BimIt.Revit.Contracts;
using Xunit;

namespace BimIt.Revit.ApiAccessSpike.Tests;

public sealed class ApiAccessSpikeTests
{
    private static readonly (string TypeName, string[] Properties)[] Benchmarks =
    [
        ("Autodesk.Revit.DB.Level", ["Name", "Elevation"]),
        ("Autodesk.Revit.DB.Phase", ["Name"]),
        ("Autodesk.Revit.DB.DesignOption", ["Name", "IsPrimary"])
    ];

    [Fact]
    public void Spike_registry_adds_two_experimental_operations_beside_user_worksets()
    {
        var operations = ReadOperationComposition.ProductionOperations();
        Assert.Equal(
            [
                "experimental.revit_api_lookup",
                "experimental.revit_api_read",
                "project.user_worksets",
                "revit_api.element_properties",
                "revit_api.metadata",
                "revit_api.symbols"
            ],
            operations.Select(operation => operation.Descriptor.Id).Order(StringComparer.Ordinal).ToArray());
        Assert.All(operations, operation => Assert.Equal(1, operation.Descriptor.ContractVersion));

        var registry = new ReadOperationRegistry(operations);
        var search = registry.Search("revit_api", 0, 10);
        Assert.Equal(5, search.Total);
        Assert.All(search.Operations, operation =>
        {
            Assert.Null(operation.InputSchema);
            Assert.Null(operation.OutputSchema);
        });

        var exact = registry.LookupExact(ExperimentalRevitApiReadReadOperation.OperationId, 1);
        Assert.Equal(ReadOperationResolution.Found, exact.Resolution);
        Assert.NotNull(exact.Result);
        Assert.NotNull(exact.Result.Operations[0].InputSchema);
        Assert.NotNull(exact.Result.Operations[0].OutputSchema);
        Assert.Equal(27, BridgeProtocol.CurrentVersion);
        Assert.True(BridgeProtocol.SupportsReadOperations(24));
        Assert.False(BridgeProtocol.SupportsReadOperations(23));

        var spikeDll = Path.Combine(
            FindRepoRoot(),
            "src",
            "BimIt.Revit.Addin",
            "bin",
            "2026",
            "Spike",
            "Release",
            "net8.0-windows",
            "BimIt.Revit.Addin.dll");
        var image = File.ReadAllBytes(spikeDll);
        Assert.True(ContainsUtf16(image, "experimental.revit_api_lookup"));
        Assert.True(ContainsUtf16(image, "experimental.revit_api_read"));
    }

    [Fact]
    public void The_same_resolver_accepts_every_benchmark_type_and_rejects_the_boundaries()
    {
        using var revit = OpenRevitApi(out var assembly);
        foreach (var benchmark in Benchmarks)
        {
            var type = TrustedRevitApiTypes.RequireConcreteElement(assembly, benchmark.TypeName);
            var resolved = ScalarPropertyProjection.RequireProjectable(assembly, type, benchmark.Properties);
            Assert.Equal(benchmark.Properties.Length, resolved.Properties.Count);
            var described = RevitApiMetadataCatalog.Describe(assembly, type);
            Assert.True(described.CanCollect);
            Assert.True(described.IsElement);
            Assert.False(described.IsAbstract);
            foreach (var propertyName in benchmark.Properties)
            {
                var property = Assert.Single(described.Properties, item => item.Name == propertyName);
                Assert.True(property.CanProject);
            }

            Assert.All(described.Methods, method => Assert.False(method.Executable));
            var unsupported = Assert.Throws<BridgeException>(() => ScalarPropertyProjection.RequireProjectable(assembly, type, ["Id"]));
            Assert.Equal(CapabilityErrorCodes.InvalidReadOperationInput, unsupported.ErrorCode);
            Assert.Contains(ScalarPropertyProjection.UnsupportedReturn, unsupported.Message, StringComparison.Ordinal);
        }

        var unknownType = Assert.Throws<BridgeException>(() => TrustedRevitApiTypes.RequireConcreteElement(assembly, "Autodesk.Revit.DB.NotARealType"));
        Assert.Equal(CapabilityErrorCodes.InvalidReadOperationInput, unknownType.ErrorCode);
        var element = Assert.Throws<BridgeException>(() => TrustedRevitApiTypes.RequireConcreteElement(assembly, "Autodesk.Revit.DB.Element"));
        Assert.Equal(CapabilityErrorCodes.InvalidReadOperationInput, element.ErrorCode);
        var value = Assert.Throws<BridgeException>(() => TrustedRevitApiTypes.RequireConcreteElement(assembly, "Autodesk.Revit.DB.XYZ"));
        Assert.Equal(CapabilityErrorCodes.InvalidReadOperationInput, value.ErrorCode);

        var level = TrustedRevitApiTypes.RequireConcreteElement(assembly, Benchmarks[0].TypeName);
        var unknownProperty = Assert.Throws<BridgeException>(() => ScalarPropertyProjection.RequireProjectable(assembly, level, ["NotAProperty"]));
        Assert.Equal(CapabilityErrorCodes.InvalidReadOperationInput, unknownProperty.ErrorCode);
    }

    [Fact]
    public void Metadata_search_finds_benchmark_members_without_returning_executable_methods()
    {
        using var revit = OpenRevitApi(out var assembly);
        var elevation = RevitApiMetadataCatalog.Search(assembly, "Elevation");
        Assert.Contains(elevation, type => type.TypeName == Benchmarks[0].TypeName && type.Properties.Count == 0 && type.CanCollect);
        var primary = RevitApiMetadataCatalog.Search(assembly, "IsPrimary");
        var match = Assert.Single(primary, type => type.TypeName == Benchmarks[2].TypeName);
        Assert.Contains("IsPrimary", match.MatchedMembers);
        Assert.Empty(match.Methods);

        var described = RevitApiMetadataCatalog.Describe(assembly, TrustedRevitApiTypes.FindPublicType(assembly, Benchmarks[2].TypeName)!);
        Assert.Contains(described.Properties, property => property.Name == "IsPrimary" && property.CanProject);
        Assert.All(described.Methods, method => Assert.False(method.Executable));
    }

    [Fact]
    public void Projection_rules_reject_setters_indexers_statics_and_foreign_assemblies()
    {
        var trusted = typeof(ProjectionProbe).Assembly;
        var readOnly = ScalarPropertyProjection.Describe(Property(nameof(ProjectionProbe.ReadOnly)), trusted);
        var readWrite = ScalarPropertyProjection.Describe(Property(nameof(ProjectionProbe.ReadWrite)), trusted);
        var writeOnly = ScalarPropertyProjection.Describe(Property(nameof(ProjectionProbe.WriteOnly)), trusted);
        var indexer = ScalarPropertyProjection.Describe(
            typeof(ProjectionProbe).GetProperties().Single(property => property.GetIndexParameters().Length > 0),
            trusted);
        var staticProperty = ScalarPropertyProjection.Describe(
            typeof(ProjectionProbe).GetProperty(nameof(ProjectionProbe.StaticText), BindingFlags.Public | BindingFlags.Static)!,
            trusted);
        var reference = ScalarPropertyProjection.Describe(Property(nameof(ProjectionProbe.Reference)), trusted);
        var outside = ScalarPropertyProjection.Describe(Property(nameof(ProjectionProbe.ReadOnly)), typeof(string).Assembly);

        Assert.True(readOnly.CanProject);
        Assert.True(readWrite.CanProject);
        Assert.Equal(ScalarPropertyProjection.NotReadable, writeOnly.Rejection);
        Assert.Equal(ScalarPropertyProjection.Indexer, indexer.Rejection);
        Assert.Equal(ScalarPropertyProjection.StaticProperty, staticProperty.Rejection);
        Assert.Equal(ScalarPropertyProjection.UnsupportedReturn, reference.Rejection);
        Assert.Equal(ScalarPropertyProjection.OutsideAssembly, outside.Rejection);

        Assert.Equal("string", ScalarPropertyProjection.Encode("ReadOnly", typeof(string), "North").Kind);
        Assert.Equal("boolean", ScalarPropertyProjection.Encode("Flag", typeof(bool), false).Kind);
        Assert.False(ScalarPropertyProjection.Encode("Flag", typeof(bool), false).Boolean);
        Assert.Equal(3L, ScalarPropertyProjection.Encode("Count", typeof(int), 3).Integer);
        Assert.Equal("number", ScalarPropertyProjection.Encode("Amount", typeof(double), 1.5d).Kind);
        Assert.Equal("enum", ScalarPropertyProjection.Encode("Comparison", typeof(StringComparison), StringComparison.Ordinal).Kind);
        Assert.Equal("Ordinal", ScalarPropertyProjection.Encode("Comparison", typeof(StringComparison), StringComparison.Ordinal).Text);
        Assert.Equal("null", ScalarPropertyProjection.Encode("Text", typeof(string), null).Kind);
        var nonFinite = Assert.Throws<BridgeException>(() => ScalarPropertyProjection.Encode("Amount", typeof(double), double.NaN));
        Assert.Equal(CapabilityErrorCodes.InvalidReadOperationInput, nonFinite.ErrorCode);
    }

    [Fact]
    public void Input_schemas_reject_unknown_fields_and_incomplete_requests()
    {
        var lookup = new ExperimentalRevitApiLookupReadOperation();
        var read = new ExperimentalRevitApiReadReadOperation();
        Assert.True(Matches(lookup, """{"query":"El"}"""));
        Assert.False(Matches(lookup, """{"query":"El","other":true}"""));
        var empty = lookup.Bind(Parse("{}"));
        var missing = Assert.Throws<BridgeException>(() => lookup.ValidateInputs(empty));
        Assert.Equal(CapabilityErrorCodes.InvalidReadOperationInput, missing.ErrorCode);
        Assert.True(Matches(read, """{"type_name":"Autodesk.Revit.DB.Element","properties":["Name"]}"""));
        Assert.False(Matches(read, """{"type_name":"Autodesk.Revit.DB.Element","properties":["Name"],"script":"return 1;"}"""));
        Assert.False(Matches(read, """{"type_name":"Autodesk.Revit.DB.Element","properties":[]}"""));

        var both = lookup.Bind(Parse("""{"query":"El","type_name":"Autodesk.Revit.DB.Element"}"""));
        var rejected = Assert.Throws<BridgeException>(() => lookup.ValidateInputs(both));
        Assert.Equal(CapabilityErrorCodes.InvalidReadOperationInput, rejected.ErrorCode);
    }

    [Fact]
    public void Spike_sources_have_no_benchmark_branch_transaction_or_arbitrary_invocation()
    {
        var root = FindRepoRoot();
        var spike = Path.Combine(root, "spikes", "BimIt.Revit.ApiAccessSpike");
        var sources = Directory.GetFiles(spike, "*.cs").Select(path => File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal)).ToArray();
        var combined = string.Join("\n", sources);
        foreach (var pattern in new[] { @"\bLevel\b", @"\bPhases?\b", @"\bDesignOption\b" })
        {
            Assert.DoesNotMatch(new Regex(pattern, RegexOptions.CultureInvariant), combined);
        }

        foreach (var forbidden in new[]
        {
            "Transaction",
            "SubTransaction",
            "TransactionGroup",
            "Assembly.Load",
            "SetMethod",
            "CSharpCodeProvider",
            "AssemblyBuilder",
            "OfClass(typeof"
        })
        {
            Assert.DoesNotContain(forbidden, combined, StringComparison.Ordinal);
        }

        Assert.Equal(1, Count(combined, ".Invoke("));
        Assert.Contains("getter.Invoke(target, null)", combined, StringComparison.Ordinal);
        Assert.Contains(".OfClass(elementType)", combined, StringComparison.Ordinal);

        var composition = File.ReadAllText(Path.Combine(root, "src", "BimIt.Revit.Addin", "ReadOperations", "ReadOperationComposition.cs"));
        var gate = composition.IndexOf("#if BIMIT_API_ACCESS_SPIKE", StringComparison.Ordinal);
        var end = composition.IndexOf("#endif", gate, StringComparison.Ordinal);
        Assert.True(gate >= 0 && end > gate);
        Assert.Contains("ApiAccessSpikeOperations.AddExperimental", composition.Substring(gate, end - gate), StringComparison.Ordinal);

        var server = string.Join("\n", Directory.GetFiles(Path.Combine(root, "src", "BimIt.Revit.Server"), "*.cs").Select(File.ReadAllText));
        Assert.DoesNotContain("experimental.revit_api_lookup", server, StringComparison.Ordinal);
        Assert.DoesNotContain("experimental.revit_api_read", server, StringComparison.Ordinal);
    }

    private static bool ContainsUtf16(byte[] bytes, string value)
    {
        var needle = System.Text.Encoding.Unicode.GetBytes(value);
        return IndexOf(bytes, needle, 0) >= 0 || IndexOf(bytes, needle, 1) >= 0;
    }

    private static int IndexOf(byte[] bytes, byte[] needle, int start)
    {
        for (var index = start; index <= bytes.Length - needle.Length; index++)
        {
            var matched = true;
            for (var offset = 0; offset < needle.Length; offset++)
            {
                if (bytes[index + offset] != needle[offset])
                {
                    matched = false;
                    break;
                }
            }

            if (matched)
            {
                return index;
            }
        }

        return -1;
    }

    private static MetadataLoadContext OpenRevitApi(out Assembly assembly)
    {
        var revit = Path.Combine(AppContext.BaseDirectory, "RevitAPI.dll");
        var runtime = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var paths = Directory.GetFiles(runtime, "*.dll").Append(revit);
        var context = new MetadataLoadContext(new PathAssemblyResolver(paths));
        assembly = context.LoadFromAssemblyPath(revit);
        return context;
    }

    private static PropertyInfo Property(string name)
    {
        return typeof(ProjectionProbe).GetProperty(name, BindingFlags.Public | BindingFlags.Instance)!;
    }

    private static bool Matches(IRevitReadOperation operation, string json)
    {
        return JsonSchemaSubset.TryMatchInstance(Parse(json), operation.Descriptor.InputSchema!.Value, out _);
    }

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
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

    private sealed class ProjectionProbe
    {
        public string ReadOnly { get; } = string.Empty;

        public string ReadWrite { get; set; } = string.Empty;

        public string WriteOnly
        {
            set
            {
            }
        }

        public static string StaticText { get; } = string.Empty;

        public object Reference { get; } = new object();

        public string this[int index] => string.Empty;
    }
}
