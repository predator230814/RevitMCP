using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using BimIt.Revit.Addin.Query;
using BimIt.Revit.Addin.ReadOperations;
using BimIt.Revit.Bridge;
using BimIt.Revit.Contracts;
using Xunit;

namespace BimIt.Revit.Addin.Tests;

public sealed class RevitApiPropertyAccessTests
{
    private static readonly (string TypeName, string[] Properties)[] Benchmarks =
    [
        ("Autodesk.Revit.DB.Level", ["Name", "Elevation"]),
        ("Autodesk.Revit.DB.Phase", ["Name"]),
        ("Autodesk.Revit.DB.DesignOption", ["Name", "IsPrimary"])
    ];

    [Fact]
    public void Plural_equivalence_matches_the_accepted_pairs_and_does_not_stem()
    {
        Assert.Equal(["design", "option"], RevitApiTokens.Tokenize("DesignOption"));
        Assert.Equal(["is", "primary"], RevitApiTokens.Tokenize("IsPrimary"));
        Assert.Equal(["level"], RevitApiTokens.Tokenize("Level"));
        Assert.Equal(["design", "options"], RevitApiTokens.Tokenize("design options"));
        Assert.Equal(["classes"], RevitApiTokens.Tokenize("classes"));
        Assert.Equal(["properties"], RevitApiTokens.Tokenize("properties"));

        Assert.True(RevitApiTokens.Matches("levels", "level"));
        Assert.True(RevitApiTokens.Matches("phases", "phase"));
        Assert.True(RevitApiTokens.Matches("options", "option"));
        Assert.True(RevitApiTokens.Matches("classes", "class"));
        Assert.True(RevitApiTokens.Matches("properties", "property"));
        Assert.True(RevitApiTokens.Matches("elevation", "elevation"));
        Assert.True(RevitApiTokens.Matches("primary", "primary"));
        Assert.False(RevitApiTokens.Matches("classe", "class"));
        Assert.False(RevitApiTokens.Matches("propertie", "property"));
        Assert.True(RevitApiTokens.MatchesAll(["design", "options"], ["design", "option"]));
    }

    [Fact]
    public void The_same_resolver_accepts_every_benchmark_type()
    {
        using var revit = OpenRevitApi(out var assembly);
        foreach (var benchmark in Benchmarks)
        {
            var type = RevitApiTypeResolver.RequireForRead(assembly, benchmark.TypeName);
            var resolved = RevitApiPropertyPolicy.RequireProjectable(assembly, type, benchmark.Properties);
            Assert.Equal(benchmark.Properties.Length, resolved.Count);
            var described = RevitApiMetadataIndex.Describe(assembly, type);
            Assert.True(described.IsElement);
            Assert.False(described.IsAbstract);
            Assert.False(string.IsNullOrEmpty(described.BaseTypeName));
            foreach (var propertyName in benchmark.Properties)
            {
                var property = Assert.Single(described.Properties, item => item.Name == propertyName);
                Assert.True(property.CanProject);
                Assert.Null(property.Rejection);
            }

            var unsupported = Assert.Throws<BridgeException>(() =>
                RevitApiPropertyPolicy.RequireProjectable(assembly, type, ["Id"]));
            Assert.Equal(CapabilityErrorCodes.InvalidReadOperationInput, unsupported.ErrorCode);
            Assert.Contains(RevitApiPropertyPolicy.UnsupportedReturn, unsupported.Message, StringComparison.Ordinal);
        }

        AssertTypeName(assembly, "levels", "Autodesk.Revit.DB.Level");
        AssertTypeName(assembly, "phases", "Autodesk.Revit.DB.Phase");
        AssertTypeName(assembly, "design options", "Autodesk.Revit.DB.DesignOption");
        var elevation = Assert.Single(
            RevitApiMetadataIndex.Search(assembly, "elevation"),
            type => type.TypeName == Benchmarks[0].TypeName);
        Assert.Contains("Elevation", elevation.MatchedMembers);
        Assert.Empty(elevation.Properties);
        Assert.True(elevation.IsElement);
        var primary = Assert.Single(
            RevitApiMetadataIndex.Search(assembly, "primary"),
            type => type.TypeName == Benchmarks[2].TypeName);
        Assert.Contains("IsPrimary", primary.MatchedMembers);
        var narrowed = Assert.Single(RevitApiMetadataIndex.Search(assembly, "project elevation"));
        Assert.Equal(Benchmarks[0].TypeName, narrowed.TypeName);

        var exact = RevitApiMetadataIndex.DescribeExact(assembly, Benchmarks[0].TypeName);
        Assert.Equal(Benchmarks[0].TypeName, exact.TypeName);
        Assert.Contains(exact.Properties, property => property.Name == "Elevation" && property.CanProject);
        var value = RevitApiMetadataIndex.DescribeExact(assembly, "Autodesk.Revit.DB.XYZ");
        Assert.False(value.IsElement);
        Assert.Empty(value.Properties);
        Assert.Empty(value.MatchedMembers);

        var element = RevitApiMetadataIndex.Describe(
            assembly,
            RevitApiTypeResolver.FindPublicType(assembly, "Autodesk.Revit.DB.Element")!);
        Assert.True(element.IsElement);
        Assert.Null(element.BaseTypeName);

        AssertRejected(assembly, "Autodesk.Revit.DB.NotARealType", "unknown");
        AssertRejected(assembly, "autodesk.revit.db.level", "unknown");
        AssertRejected(assembly, "Autodesk.Revit.DB.XYZ", "not_element");

        var emptyTokens = Assert.Throws<BridgeException>(() => RevitApiMetadataIndex.Search(assembly, "!!"));
        Assert.Equal(CapabilityErrorCodes.InvalidReadOperationInput, emptyTokens.ErrorCode);
        Assert.Contains("query", emptyTokens.Message, StringComparison.Ordinal);
        Assert.Empty(RevitApiMetadataIndex.Search(assembly, "zzzznotarealtype"));
    }

    [Fact]
    public void Property_policy_accepts_scalars_and_rejects_the_accepted_boundaries()
    {
        var trusted = typeof(ProjectionProbe).Assembly;
        Assert.True(RevitApiPropertyPolicy.Describe(Property(nameof(ProjectionProbe.ReadOnly)), trusted).CanProject);
        Assert.True(RevitApiPropertyPolicy.Describe(Property(nameof(ProjectionProbe.ReadWrite)), trusted).CanProject);
        Assert.Equal(RevitApiPropertyPolicy.NotReadable, RevitApiPropertyPolicy.Describe(Property(nameof(ProjectionProbe.WriteOnly)), trusted).Rejection);
        Assert.Equal(
            RevitApiPropertyPolicy.Indexer,
            RevitApiPropertyPolicy.Describe(typeof(ProjectionProbe).GetProperties().Single(property => property.GetIndexParameters().Length > 0), trusted).Rejection);
        Assert.Equal(
            RevitApiPropertyPolicy.StaticProperty,
            RevitApiPropertyPolicy.Describe(typeof(ProjectionProbe).GetProperty(nameof(ProjectionProbe.StaticText), BindingFlags.Public | BindingFlags.Static)!, trusted).Rejection);
        Assert.Equal(RevitApiPropertyPolicy.UnsupportedReturn, RevitApiPropertyPolicy.Describe(Property(nameof(ProjectionProbe.Reference)), trusted).Rejection);
        Assert.Equal(RevitApiPropertyPolicy.UnsupportedReturn, RevitApiPropertyPolicy.Describe(Property(nameof(ProjectionProbe.Flags)), trusted).Rejection);
        Assert.Equal(RevitApiPropertyPolicy.UnsupportedReturn, RevitApiPropertyPolicy.Describe(Property(nameof(ProjectionProbe.Wide)), trusted).Rejection);
        Assert.Equal(
            RevitApiPropertyPolicy.OutsideAssembly,
            RevitApiPropertyPolicy.Describe(Property(nameof(ProjectionProbe.ReadOnly)), typeof(string).Assembly).Rejection);

        Assert.Equal("boolean", RevitApiPropertyPolicy.Encode("Flag", typeof(bool), false).Kind);
        Assert.False(RevitApiPropertyPolicy.Encode("Flag", typeof(bool), false).Boolean);
        Assert.Equal("null", RevitApiPropertyPolicy.Encode("Text", typeof(string), null).Kind);
        Assert.Equal(RevitApiPropertyPolicy.MaxSafeInteger, RevitApiPropertyPolicy.Encode("Count", typeof(long), RevitApiPropertyPolicy.MaxSafeInteger).Integer);
        var range = Assert.Throws<BridgeException>(() => RevitApiPropertyPolicy.Encode("Count", typeof(long), RevitApiPropertyPolicy.MaxSafeInteger + 1));
        Assert.Equal(CapabilityErrorCodes.ExecutionFailed, range.ErrorCode);
        Assert.Contains("integer_out_of_range", range.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(RevitApiPropertyPolicy.MaxSafeInteger.ToString(), range.Message, StringComparison.Ordinal);
        var nonFinite = Assert.Throws<BridgeException>(() => RevitApiPropertyPolicy.Encode("Amount", typeof(double), double.NaN));
        Assert.Contains("non_finite_number", nonFinite.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("NaN", nonFinite.Message, StringComparison.Ordinal);
        var unnamed = Assert.Throws<BridgeException>(() => RevitApiPropertyPolicy.Encode("Comparison", typeof(StringComparison), (StringComparison)999));
        Assert.Contains("unnamed_enum", unnamed.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("999", unnamed.Message, StringComparison.Ordinal);
        var tooLong = new string('x', RevitApiPropertyPolicy.MaxStringLength + 1);
        var length = Assert.Throws<BridgeException>(() => RevitApiPropertyPolicy.Encode("Text", typeof(string), tooLong));
        Assert.Equal(CapabilityErrorCodes.ResultTooLarge, length.ErrorCode);
        Assert.DoesNotContain("xxx", length.Message, StringComparison.Ordinal);

        var encoded = RevitApiPropertyPolicy.Encode("Text", typeof(string), "North");
        using var document = JsonDocument.Parse(ReadResultBudget.Serialize(encoded));
        Assert.Equal("string", document.RootElement.GetProperty("kind").GetString());
        Assert.Equal("North", document.RootElement.GetProperty("text").GetString());
        Assert.False(document.RootElement.TryGetProperty("boolean", out _));
        Assert.False(document.RootElement.TryGetProperty("number", out _));
        Assert.False(document.RootElement.TryGetProperty("unit", out _));
    }

    [Fact]
    public void Listing_caps_keep_every_projectable_property_until_the_accepted_limit()
    {
        var members = Enumerable.Range(0, 10).Select(index => "M" + index.ToString("00")).ToArray();
        var limited = RevitApiMetadataIndex.LimitMembers(members);
        Assert.Equal(8, limited.Members.Count);
        Assert.Equal(2, limited.Omitted);
        Assert.Equal("M00", limited.Members[0]);

        var projectable = Enumerable.Range(0, 2).Select(index => PropertyModel(index)).ToArray();
        var rejected = Enumerable.Range(0, 90).Select(index => PropertyModel(index, projectable: false)).ToArray();
        var listed = RevitApiMetadataIndex.LimitProperties(projectable, rejected);
        Assert.Equal(80, listed.Listed.Count);
        Assert.Equal(12, listed.RejectedOmitted);
        Assert.True(listed.Listed[0].CanProject);
        Assert.True(listed.Listed[1].CanProject);
        Assert.False(listed.Listed[2].CanProject);

        var tooMany = Enumerable.Range(0, 81).Select(index => PropertyModel(index)).ToArray();
        var overflow = Assert.Throws<BridgeException>(() =>
            RevitApiMetadataIndex.LimitProperties(tooMany, Array.Empty<RevitApiMetadataProperty>()));
        Assert.Equal(CapabilityErrorCodes.ResultTooLarge, overflow.ErrorCode);
    }

    [Fact]
    public void Registered_operations_use_the_accepted_contract_and_existing_tools()
    {
        var metadata = new RevitApiMetadataReadOperation();
        var read = new RevitApiElementPropertiesReadOperation();
        Assert.Equal("Revit API element metadata", metadata.Descriptor.Title);
        Assert.Equal("Read element properties", read.Descriptor.Title);
        Assert.Equal("document", metadata.Descriptor.Context);
        Assert.Equal("read", metadata.Descriptor.Effects);
        Assert.Equal("cursor", metadata.Descriptor.Pagination);
        Assert.Contains("no physical unit", read.Descriptor.Description, StringComparison.Ordinal);
        foreach (var description in new[] { metadata.Descriptor.Description, read.Descriptor.Description })
        {
            Assert.DoesNotMatch(new Regex(@"\bLevel\b|\bPhases?\b|\bDesignOption\b", RegexOptions.CultureInvariant), description);
        }

        Assert.True(JsonSchemaSubset.TryValidateOperationSchema(metadata.Descriptor.InputSchema!.Value, out _));
        Assert.True(JsonSchemaSubset.TryValidateOperationSchema(metadata.Descriptor.OutputSchema!.Value, out _));
        Assert.True(JsonSchemaSubset.TryValidateOperationSchema(read.Descriptor.InputSchema!.Value, out _));
        Assert.True(JsonSchemaSubset.TryValidateOperationSchema(read.Descriptor.OutputSchema!.Value, out _));
        Assert.Contains("\"element_ref\"", RevitApiElementPropertiesReadOperation.OutputSchemaJson, StringComparison.Ordinal);
        Assert.DoesNotContain("\"unique_id\"", RevitApiElementPropertiesReadOperation.OutputSchemaJson, StringComparison.Ordinal);
        Assert.DoesNotContain("\"element_id\"", RevitApiElementPropertiesReadOperation.OutputSchemaJson, StringComparison.Ordinal);
        Assert.DoesNotContain("\"methods\"", RevitApiMetadataReadOperation.OutputSchemaJson, StringComparison.Ordinal);
        Assert.DoesNotContain("\"can_collect\"", RevitApiMetadataReadOperation.OutputSchemaJson, StringComparison.Ordinal);

        Assert.True(Matches(metadata, """{"query":"El"}"""));
        Assert.False(Matches(metadata, """{"query":"El","script":"return 1;"}"""));
        var both = metadata.Bind(Parse("""{"query":"El","type_name":"Autodesk.Revit.DB.Element"}"""));
        var rejected = Assert.Throws<BridgeException>(() => metadata.ValidateInputs(both));
        Assert.Equal(CapabilityErrorCodes.InvalidReadOperationInput, rejected.ErrorCode);
        var neither = metadata.Bind(Parse("{}"));
        Assert.Equal(CapabilityErrorCodes.InvalidReadOperationInput, Assert.Throws<BridgeException>(() => metadata.ValidateInputs(neither)).ErrorCode);

        Assert.True(Matches(read, """{"type_name":"Autodesk.Revit.DB.Element","properties":["Name"]}"""));
        Assert.False(Matches(read, """{"type_name":"Autodesk.Revit.DB.Element","properties":["Name"],"script":"return 1;"}"""));
        Assert.False(Matches(read, """{"type_name":"Autodesk.Revit.DB.Element","properties":[]}"""));
        var dotted = read.Bind(Parse("""{"type_name":"Autodesk.Revit.DB.Element","properties":["A.B"]}"""));
        Assert.Equal(CapabilityErrorCodes.InvalidReadOperationInput, Assert.Throws<BridgeException>(() => read.ValidateInputs(dotted)).ErrorCode);

        var forward = read.Bind(Parse("""{"type_name":"Autodesk.Revit.DB.Element","properties":["Name","Id"]}"""));
        var reverse = read.Bind(Parse("""{"type_name":"Autodesk.Revit.DB.Element","properties":["Id","Name"]}"""));
        read.ValidateInputs(forward);
        read.ValidateInputs(reverse);
        Assert.False(read.Fingerprint(forward, "doc", read.Descriptor).SequenceEqual(read.Fingerprint(reverse, "doc", read.Descriptor)));
        var levelsQuery = metadata.Bind(Parse("""{"query":"levels"}"""));
        var levelQuery = metadata.Bind(Parse("""{"query":"Levels"}"""));
        metadata.ValidateInputs(levelsQuery);
        metadata.ValidateInputs(levelQuery);
        Assert.False(metadata.Fingerprint(levelsQuery, "doc", metadata.Descriptor).SequenceEqual(
            metadata.Fingerprint(levelQuery, "doc", metadata.Descriptor)));

        var registry = new ReadOperationRegistry(ReadOperationComposition.ProductionOperations());
        Assert.Equal(ReadOperationResolution.Found, registry.LookupExact(RevitApiMetadataReadOperation.OperationId, 1).Resolution);
        Assert.Equal(ReadOperationResolution.Found, registry.LookupExact(RevitApiElementPropertiesReadOperation.OperationId, 1).Resolution);
        Assert.Equal(26, BridgeProtocol.CurrentVersion);
        Assert.True(BridgeProtocol.SupportsReadOperations(24));
        Assert.False(BridgeProtocol.SupportsReadOperations(23));

        var invalid = new RevitApiElementPropertiesResult
        {
            Summary = new RevitApiElementPropertiesSummary { TypeName = "Autodesk.Revit.DB.Element", MatchedElementCount = 1 },
            ReturnedCount = 1,
            Elements =
            [
                new RevitApiElementRow
                {
                    ElementRef = "ref",
                    Values = [new RevitApiScalar { Name = "Name", Kind = "string", Text = "A", Number = 1 }]
                }
            ]
        };
        Assert.Equal(
            CapabilityErrorCodes.ReadOperationResultInvalid,
            Assert.Throws<BridgeException>(() => read.ValidateResult(invalid)).ErrorCode);

        var reader = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "BimIt.Revit.Addin", "ReadOperations", "RevitApiElementReader.cs"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        var ofClass = reader.IndexOf("selected = collector.OfClass(elementType);", StringComparison.Ordinal);
        var rejection = reader.IndexOf("catch (Autodesk.Revit.Exceptions.ArgumentException)", StringComparison.Ordinal);
        var reason = reader.IndexOf("Reject(\"not_collectable\")", rejection, StringComparison.Ordinal);
        var materialize = reader.IndexOf("selected.ToElements()", StringComparison.Ordinal);
        Assert.True(ofClass >= 0 && rejection > ofClass && reason > rejection && materialize > reason);
        Assert.DoesNotContain(".Message", reader, StringComparison.Ordinal);
        Assert.DoesNotContain("native object model", reader, StringComparison.Ordinal);
    }

    [Fact]
    public void Production_sources_do_not_special_case_a_class_or_add_a_tool()
    {
        var root = FindRepoRoot();
        var folder = Path.Combine(root, "src", "BimIt.Revit.Addin", "ReadOperations");
        var sources = Directory.GetFiles(folder, "*.cs").Select(path => File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal)).ToArray();
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
            "RoomFilter",
            "SpaceFilter",
            "ElementClassFilter",
            "WhereElementIsNotElementType",
            "OfClass(typeof",
            "experimental.revit_api_lookup",
            "experimental.revit_api_read",
            "\"unique_id\"",
            "\"element_id\""
        })
        {
            Assert.DoesNotContain(forbidden, combined, StringComparison.Ordinal);
        }

        Assert.Equal(1, Count(combined, ".Invoke("));
        Assert.Equal(1, Count(combined, ".OfClass("));
        Assert.Contains("getter.Invoke(target, null)", combined, StringComparison.Ordinal);
        Assert.Contains(".OfClass(elementType)", combined, StringComparison.Ordinal);

        var metadata = File.ReadAllText(Path.Combine(folder, "RevitApiMetadataReadOperation.cs"));
        Assert.DoesNotContain("IsModifiable", metadata, StringComparison.Ordinal);
        Assert.DoesNotContain("OfClass", metadata, StringComparison.Ordinal);
        Assert.DoesNotContain(".Invoke(", metadata, StringComparison.Ordinal);
        Assert.DoesNotContain("FilteredElementCollector", metadata, StringComparison.Ordinal);

        var resolver = File.ReadAllText(Path.Combine(folder, "RevitApiTypeResolver.cs"));
        var abstractCheck = resolver.IndexOf("type.IsAbstract", StringComparison.Ordinal);
        var collectorMention = resolver.IndexOf("OfClass", StringComparison.Ordinal);
        Assert.True(abstractCheck >= 0);
        Assert.Equal(-1, collectorMention);

        var reader = File.ReadAllText(Path.Combine(folder, "RevitApiElementReader.cs"));
        var before = reader.IndexOf("document.IsModifiable", StringComparison.Ordinal);
        var ofClass = reader.IndexOf(".OfClass(elementType)", StringComparison.Ordinal);
        var rejection = reader.IndexOf("catch (Autodesk.Revit.Exceptions.ArgumentException)", StringComparison.Ordinal);
        var materialize = reader.IndexOf(".ToElements()", StringComparison.Ordinal);
        var after = reader.LastIndexOf("document.IsModifiable", StringComparison.Ordinal);
        Assert.True(before >= 0 && before < ofClass && rejection > ofClass && materialize > rejection && after > materialize);
        Assert.DoesNotContain(".Message", reader, StringComparison.Ordinal);

        var operation = File.ReadAllText(Path.Combine(folder, "RevitApiElementPropertiesReadOperation.cs"));
        var typeCheck = operation.IndexOf("RequireForRead", StringComparison.Ordinal);
        var propertyCheck = operation.IndexOf("RequireProjectable", StringComparison.Ordinal);
        var collect = operation.IndexOf("RevitApiElementReader.Collect", StringComparison.Ordinal);
        var read = operation.IndexOf("RevitApiPropertyPolicy.Read", StringComparison.Ordinal);
        Assert.True(typeCheck >= 0 && propertyCheck > typeCheck && collect > propertyCheck && read > collect);

        var host = File.ReadAllText(Path.Combine(root, "src", "BimIt.Revit.Server", "ServerHost.cs"));
        Assert.Equal(22, Regex.Matches(host, @"\.With[A-Za-z]+Tool\(\)").Count);
        var server = string.Join("\n", Directory.GetFiles(Path.Combine(root, "src", "BimIt.Revit.Server"), "*.cs").Select(File.ReadAllText));
        Assert.DoesNotContain("revit_api.metadata", server, StringComparison.Ordinal);
        Assert.DoesNotContain("revit_api.element_properties", server, StringComparison.Ordinal);
        Assert.DoesNotContain("revit_api.symbols", server, StringComparison.Ordinal);
        Assert.Contains("CapabilityErrorCodes.RevitApiTypeRejected", File.ReadAllText(Path.Combine(root, "src", "BimIt.Revit.Server", "ReadOperationApplicationService.cs")), StringComparison.Ordinal);
        Assert.Contains("CapabilityErrorCodes.DocumentIsModifiable", File.ReadAllText(Path.Combine(root, "src", "BimIt.Revit.Server", "LookupReadOperationsApplicationService.cs")), StringComparison.Ordinal);
        Assert.Equal(26, BridgeProtocol.CurrentVersion);
    }

    private static void AssertTypeName(Assembly assembly, string query, string typeName)
    {
        var matches = RevitApiMetadataIndex.Search(assembly, query);
        var match = Assert.Single(matches, type => type.TypeName == typeName);
        Assert.Empty(match.MatchedMembers);
        Assert.Empty(match.Properties);
        Assert.True(match.IsElement);
    }

    private static void AssertRejected(Assembly assembly, string typeName, string reason)
    {
        var rejected = Assert.Throws<BridgeException>(() => RevitApiTypeResolver.RequireForRead(assembly, typeName));
        Assert.Equal(CapabilityErrorCodes.RevitApiTypeRejected, rejected.ErrorCode);
        Assert.Contains("(" + reason + ")", rejected.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(typeName, rejected.Message, StringComparison.Ordinal);
    }

    private static RevitApiMetadataProperty PropertyModel(int index, bool projectable = true)
    {
        return new RevitApiMetadataProperty
        {
            Name = "P" + index.ToString("00"),
            ReturnType = "System.String",
            CanProject = projectable,
            Rejection = projectable ? null : RevitApiPropertyPolicy.UnsupportedReturn
        };
    }

    private static MetadataLoadContext OpenRevitApi(out Assembly assembly)
    {
        var revit = Path.Combine(AppContext.BaseDirectory, "revit-ref", "RevitAPI.dll");
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

        public ProbeFlags Flags { get; } = ProbeFlags.None;

        public ulong Wide { get; }

        public string this[int index] => string.Empty;
    }

    [Flags]
    private enum ProbeFlags
    {
        None = 0,
        Mark = 1
    }
}
