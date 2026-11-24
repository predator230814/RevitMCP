using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using BimIt.Revit.Addin.ReadOperations;
using BimIt.Revit.Bridge;
using BimIt.Revit.Contracts;
using Xunit;

namespace BimIt.Revit.Addin.Tests;

public sealed class RevitApiSymbolDiscoveryTests
{
    private const BindingFlags Declared =
        BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private static readonly (MetadataLoadContext Context, Assembly Assembly) Loaded = LoadRevitApi();

    private static Assembly RevitApi => Loaded.Assembly;

    [Fact]
    public void Registry_search_and_exact_lookup_expose_symbols_v1()
    {
        var registry = new ReadOperationRegistry(ReadOperationComposition.ProductionOperations());
        Assert.Contains(
            registry.Search(null, 0, 20).Operations,
            operation => operation.Id == RevitApiSymbolsReadOperation.OperationId && operation.ContractVersion == 1);

        var search = registry.Search("symbols", 0, 10);
        var found = Assert.Single(search.Operations, operation => operation.Id == RevitApiSymbolsReadOperation.OperationId);
        Assert.Null(found.InputSchema);
        Assert.Null(found.OutputSchema);

        var exact = registry.LookupExact(RevitApiSymbolsReadOperation.OperationId, 1);
        Assert.Equal(ReadOperationResolution.Found, exact.Resolution);
        Assert.NotNull(exact.Result);
        Assert.NotNull(exact.Result.Operations[0].InputSchema);
        Assert.NotNull(exact.Result.Operations[0].OutputSchema);
        Assert.Equal(25, BridgeProtocol.CurrentVersion);
        Assert.Equal(22, Regex.Matches(
            File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "BimIt.Revit.Server", "ServerHost.cs")),
            @"\.With[A-Za-z]+Tool\(\)").Count);
    }

    [Fact]
    public void Inputs_are_closed_and_member_requires_an_exact_type()
    {
        var operation = new RevitApiSymbolsReadOperation();
        Assert.True(Matches(operation, """{"query":"XY"}"""));
        Assert.True(Matches(operation, """{"type_name":"Autodesk.Revit.DB.XYZ","member":"X"}"""));
        Assert.False(Matches(operation, """{"query":"X"}"""));
        Assert.False(Matches(operation, """{"query":"XY","source":"class X {}"}"""));

        var both = operation.Bind(Parse("""{"query":"XY","type_name":"Autodesk.Revit.DB.XYZ"}"""));
        var memberOnly = operation.Bind(Parse("""{"query":"XY","member":"X"}"""));
        var search = operation.Bind(Parse("""{"query":"XY"}"""));
        var exact = operation.Bind(Parse("""{"type_name":"Autodesk.Revit.DB.XYZ"}"""));
        var member = operation.Bind(Parse("""{"type_name":"Autodesk.Revit.DB.XYZ","member":"X"}"""));
        Assert.Equal(CapabilityErrorCodes.InvalidReadOperationInput, Assert.Throws<BridgeException>(() => operation.ValidateInputs(both)).ErrorCode);
        Assert.Equal(CapabilityErrorCodes.InvalidReadOperationInput, Assert.Throws<BridgeException>(() => operation.ValidateInputs(memberOnly)).ErrorCode);
        operation.ValidateInputs(search);
        operation.ValidateInputs(exact);
        operation.ValidateInputs(member);
        Assert.False(operation.Fingerprint(search, "doc", operation.Descriptor).AsSpan().SequenceEqual(operation.Fingerprint(exact, "doc", operation.Descriptor)));
        Assert.False(operation.Fingerprint(exact, "doc", operation.Descriptor).AsSpan().SequenceEqual(operation.Fingerprint(member, "doc", operation.Descriptor)));
    }

    [Fact]
    public void Search_finds_a_non_element_type_and_a_member_owner()
    {
        var types = RevitApiSymbolIndex.Search(RevitApi, "FilteredElementCollector");
        var collector = Assert.Single(types);
        Assert.Equal("Autodesk.Revit.DB.FilteredElementCollector", collector.FullName);
        Assert.Empty(collector.MatchedMembers);
        Assert.Empty(collector.Constructors);
        Assert.Equal("RevitAPI", collector.AssemblyName);
        AssertSchema(collector, "search");

        var members = RevitApiSymbolIndex.Search(RevitApi, "OfClass");
        var owner = Assert.Single(members, type => type.FullName == "Autodesk.Revit.DB.FilteredElementCollector");
        Assert.Contains("OfClass", owner.MatchedMembers);
        Assert.Equal(
            members.Select(type => type.FullName),
            members.Select(type => type.FullName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Exact_type_and_member_describe_collector_overloads()
    {
        var described = RevitApiSymbolIndex.DescribeExact(RevitApi, "Autodesk.Revit.DB.FilteredElementCollector", null);
        Assert.Equal("FilteredElementCollector", described.SimpleName);
        Assert.Equal("class", described.Kind);
        Assert.Equal("Autodesk.Revit.DB", described.Namespace);
        Assert.NotEmpty(described.Methods);
        Assert.Null(described.Member);
        AssertSchema(described, "exact");

        var reflected = RevitApi.GetType("Autodesk.Revit.DB.FilteredElementCollector")!
            .GetMethods(Declared)
            .Where(method => method.Name == "OfClass" && !method.IsSpecialName)
            .ToArray();
        var member = RevitApiSymbolIndex.DescribeExact(RevitApi, "Autodesk.Revit.DB.FilteredElementCollector", "OfClass");
        Assert.NotNull(member.Member);
        Assert.Equal("method", member.Member.Kind);
        Assert.False(member.Member.IsStatic);
        Assert.Equal(reflected.Length, member.Member.Signatures.Count + member.Member.SignaturesOmittedCount);
        Assert.NotEmpty(member.Member.Signatures);
        Assert.All(member.Member.Signatures, signature => Assert.Contains("OfClass(", signature.Text, StringComparison.Ordinal));
        Assert.Contains(member.Member.Signatures, signature => signature.Text.Contains("Type ", StringComparison.Ordinal));
        Assert.Empty(member.Methods);
        Assert.Empty(member.Constructors);
        AssertSchema(member, "exact");
        AssertNoAuthority(ReadOperationResults.Payload(Result("exact", member)));
    }

    [Fact]
    public void Exact_XYZ_and_a_static_factory_are_generic_descriptions()
    {
        var xyz = RevitApiSymbolIndex.DescribeExact(RevitApi, "Autodesk.Revit.DB.XYZ", null);
        Assert.Equal("XYZ", xyz.SimpleName);
        Assert.Equal("class", xyz.Kind);
        Assert.Equal("Autodesk.Revit.DB", xyz.Namespace);
        Assert.Contains(xyz.Properties, property => property.Text.Contains(" X ", StringComparison.Ordinal));
        Assert.Null(xyz.Member);

        var line = RevitApiSymbolIndex.DescribeExact(RevitApi, "Autodesk.Revit.DB.Line", null);
        Assert.Contains(
            line.Factories,
            factory => factory.Text.StartsWith("static ", StringComparison.Ordinal)
                && factory.Text.Contains("CreateBound(", StringComparison.Ordinal));
        Assert.DoesNotContain(line.Methods, group => group.Name == "CreateBound");
    }

    [Fact]
    public void Enum_description_is_ordinal_and_an_exact_member_keeps_one_constant()
    {
        const string typeName = "Autodesk.Revit.DB.BuiltInCategory";
        var type = RevitApi.GetType(typeName)!;
        var names = type.GetFields(Declared)
            .Select(field => field.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var described = RevitApiSymbolIndex.DescribeExact(RevitApi, typeName, null);
        var again = RevitApiSymbolIndex.DescribeExact(RevitApi, typeName, null);
        Assert.Equal("enum", described.Kind);
        Assert.Equal(RevitApiSymbolIndex.MaxFields, described.Fields.Count);
        Assert.True(described.FieldsOmittedCount > 0);
        Assert.Equal(described.Fields.Select(field => field.Text), again.Fields.Select(field => field.Text));
        for (var index = 0; index < described.Fields.Count; index++)
        {
            Assert.Contains(" " + names[index], described.Fields[index].Text, StringComparison.Ordinal);
        }

        var last = names[^1];
        Assert.DoesNotContain(described.Fields, field => field.Text.Contains(" " + last, StringComparison.Ordinal));
        var member = RevitApiSymbolIndex.DescribeExact(RevitApi, typeName, last);
        Assert.NotNull(member.Member);
        Assert.Equal("field", member.Member.Kind);
        Assert.Equal(last, member.Member.Name);
        var signature = Assert.Single(member.Member.Signatures);
        Assert.Contains(last, signature.Text, StringComparison.Ordinal);
        Assert.Equal(0, member.Member.SignaturesOmittedCount);
    }

    [Fact]
    public void Obsolete_metadata_is_bounded()
    {
        var found = FindObsolete();
        Assert.NotNull(found.Owner);
        var described = found.Member is null
            ? RevitApiSymbolIndex.DescribeExact(RevitApi, found.Owner.FullName!, null)
            : RevitApiSymbolIndex.DescribeExact(RevitApi, found.Owner.FullName!, found.Member.Name);
        var message = found.Member is null ? described.ObsoleteMessage : described.Member!.Signatures[0].ObsoleteMessage;
        var truncated = found.Member is null ? described.ObsoleteMessageTruncated : described.Member!.Signatures[0].ObsoleteMessageTruncated;
        var obsolete = found.Member is null ? described.Obsolete : described.Member!.Signatures[0].Obsolete;
        Assert.True(obsolete);
        if (found.RawMessage is null)
        {
            Assert.Null(message);
            Assert.False(truncated);
        }
        else if (found.RawMessage.Length > RevitApiSymbolIndex.MaxObsoleteMessage)
        {
            Assert.True(truncated);
            Assert.Equal(found.RawMessage[..RevitApiSymbolIndex.MaxObsoleteMessage], message);
        }
        else
        {
            Assert.False(truncated);
            Assert.Equal(found.RawMessage, message);
        }
    }

    [Fact]
    public void Simple_names_do_not_select_a_type_and_unknown_names_fail()
    {
        var types = RevitApi.GetExportedTypes()
            .Where(type => type.FullName is not null && !type.FullName.Contains('`') && !type.FullName.Contains('<'))
            .ToArray();
        var duplicate = types
            .GroupBy(type => type.Name, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .First();
        var ambiguous = Assert.Throws<BridgeException>(() => RevitApiSymbolIndex.DescribeExact(RevitApi, duplicate.Key, null));
        Assert.Equal(CapabilityErrorCodes.InvalidReadOperationInput, ambiguous.ErrorCode);
        var left = RevitApiSymbolIndex.DescribeExact(RevitApi, duplicate.First().FullName!, null);
        var right = RevitApiSymbolIndex.DescribeExact(RevitApi, duplicate.Skip(1).First().FullName!, null);
        Assert.NotEqual(left.FullName, right.FullName);

        var simple = Assert.Throws<BridgeException>(() => RevitApiSymbolIndex.DescribeExact(RevitApi, "XYZ", null));
        Assert.Equal(CapabilityErrorCodes.InvalidReadOperationInput, simple.ErrorCode);
        Assert.DoesNotContain("XYZ", simple.Message, StringComparison.Ordinal);

        var missingType = Assert.Throws<BridgeException>(() => RevitApiSymbolIndex.DescribeExact(RevitApi, "Autodesk.Revit.DB.NotARealType", null));
        Assert.Equal(CapabilityErrorCodes.InvalidReadOperationInput, missingType.ErrorCode);
        var missingMember = Assert.Throws<BridgeException>(() => RevitApiSymbolIndex.DescribeExact(RevitApi, "Autodesk.Revit.DB.XYZ", "NotARealMember"));
        Assert.Equal(CapabilityErrorCodes.InvalidReadOperationInput, missingMember.ErrorCode);
        var wrongCase = Assert.Throws<BridgeException>(() => RevitApiSymbolIndex.DescribeExact(RevitApi, "Autodesk.Revit.DB.FilteredElementCollector", "ofclass"));
        Assert.Equal(CapabilityErrorCodes.InvalidReadOperationInput, wrongCase.ErrorCode);
    }

    [Fact]
    public void Oversized_search_fails_closed_and_plural_tokens_are_not_aliases()
    {
        var error = Assert.Throws<BridgeException>(() => RevitApiSymbolIndex.Search(RevitApi, "Element"));
        Assert.Equal(CapabilityErrorCodes.InvalidReadOperationInput, error.ErrorCode);
        Assert.Contains(RevitApiSymbolIndex.MaxSearchMatches.ToString(), error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Autodesk", error.Message, StringComparison.Ordinal);
        var again = Assert.Throws<BridgeException>(() => RevitApiSymbolIndex.Search(RevitApi, "Element"));
        Assert.Equal(error.Message, again.Message);

        var walls = RevitApiSymbolIndex.Search(RevitApi, "walls");
        Assert.DoesNotContain(walls, type => type.FullName == "Autodesk.Revit.DB.Wall");
        var levels = RevitApiSymbolIndex.Search(RevitApi, "levels");
        Assert.DoesNotContain(levels, type => type.FullName == "Autodesk.Revit.DB.Level");
    }

    [Fact]
    public void Descriptions_stay_inside_the_loaded_Revit_API_assembly()
    {
        var ui = Assert.Throws<BridgeException>(() => RevitApiSymbolIndex.DescribeExact(RevitApi, "Autodesk.Revit.UI.UIApplication", null));
        Assert.Equal(CapabilityErrorCodes.InvalidReadOperationInput, ui.ErrorCode);
        var matches = RevitApiSymbolIndex.Search(RevitApi, "FilteredElementCollector");
        Assert.All(matches, type =>
        {
            Assert.Equal("RevitAPI", type.AssemblyName);
            Assert.False(type.Namespace.StartsWith("Autodesk.Revit.UI", StringComparison.Ordinal));
        });
        Assert.DoesNotContain(
            RevitApi.GetExportedTypes(),
            type => type.Namespace?.StartsWith("Autodesk.Revit.UI", StringComparison.Ordinal) == true);

        var json = ReadResultBudget.Serialize(RevitApiSymbolIndex.DescribeExact(RevitApi, "Autodesk.Revit.DB.XYZ", null));
        Assert.DoesNotContain(".dll", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RevitAPIUI", json, StringComparison.Ordinal);
        Assert.DoesNotContain("MetadataToken", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\\", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Microsoft.CodeAnalysis", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Production_symbol_code_does_not_execute_or_reference_roslyn()
    {
        var root = FindRepoRoot();
        var folder = Path.Combine(root, "src", "BimIt.Revit.Addin", "ReadOperations");
        var symbols = string.Join(
            "\n",
            new[] { "RevitApiSymbolIndex.cs", "RevitApiSymbolModels.cs", "RevitApiSymbolsReadOperation.cs" }
                .Select(name => File.ReadAllText(Path.Combine(folder, name))));
        foreach (var forbidden in new[]
        {
            "FilteredElementCollector",
            "UnitUtils",
            "ElementTransformUtils",
            "OfClass",
            "PreferredEnum",
            "Microsoft.CodeAnalysis",
            ".Invoke(",
            ".GetValue(",
            "Assembly.Load",
            "Transaction",
            "Roslyn"
        })
        {
            Assert.DoesNotContain(forbidden, symbols, StringComparison.Ordinal);
        }

        Assert.Contains("GetRawConstantValue", symbols, StringComparison.Ordinal);
        var projects = Directory.GetFiles(Path.Combine(root, "src"), "*.csproj", SearchOption.AllDirectories);
        Assert.All(projects, project => Assert.DoesNotContain("Microsoft.CodeAnalysis", File.ReadAllText(project), StringComparison.Ordinal));

        var metadata = RevitApiMetadataIndex.Search(RevitApi, "Grid");
        var grid = Assert.Single(metadata, type => type.TypeName == "Autodesk.Revit.DB.Grid");
        Assert.True(grid.IsElement);
        Assert.Empty(grid.Properties);
        Assert.Empty(grid.MatchedMembers);
    }

    private static void AssertSchema(RevitApiSymbolType type, string mode)
    {
        var operation = new RevitApiSymbolsReadOperation();
        var result = Result(mode, type);
        operation.ValidateResult(result);
        var payload = ReadOperationResults.Payload(result);
        Assert.True(
            JsonSchemaSubset.TryMatchInstance(payload, operation.Descriptor.OutputSchema!.Value, out var error),
            error is null ? "schema" : error.Path + " " + error.Keyword);
    }

    private static RevitApiSymbolResult Result(string mode, RevitApiSymbolType type)
    {
        return new RevitApiSymbolResult
        {
            Summary = new RevitApiSymbolSummary { Mode = mode, MatchedTypeCount = 1 },
            ReturnedCount = 1,
            Types = [type]
        };
    }

    private static void AssertNoAuthority(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                Assert.DoesNotContain(
                    property.Name,
                    new[] { "location", "token", "il", "source", "path", "handle", "opcode", "assembly_path" });
                AssertNoAuthority(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                AssertNoAuthority(item);
            }
        }
    }

    private static (Type? Owner, MemberInfo? Member, string? RawMessage) FindObsolete()
    {
        var types = RevitApi.GetExportedTypes()
            .Where(type => type.FullName is not null && !type.FullName.Contains('`') && !type.FullName.Contains('<'))
            .OrderBy(type => type.FullName, StringComparer.Ordinal);
        foreach (var type in types)
        {
            if (ObsoleteMessage(type, out var typeMessage))
            {
                return (type, null, typeMessage);
            }
        }

        foreach (var type in types)
        {
            MemberInfo[] members;
            try
            {
                members = type.GetMembers(Declared);
            }
            catch (Exception exception) when (exception is TypeLoadException or FileNotFoundException or FileLoadException)
            {
                continue;
            }

            foreach (var member in members.OrderBy(member => member.Name, StringComparer.Ordinal))
            {
                if (member is Type || !ObsoleteMessage(member, out var message))
                {
                    continue;
                }

                return (type, member, message);
            }
        }

        return (null, null, null);
    }

    private static bool ObsoleteMessage(MemberInfo member, out string? message)
    {
        message = null;
        try
        {
            foreach (var attribute in member.GetCustomAttributesData())
            {
                if (!string.Equals(attribute.AttributeType.Name, "ObsoleteAttribute", StringComparison.Ordinal))
                {
                    continue;
                }

                if (attribute.ConstructorArguments.Count > 0 && attribute.ConstructorArguments[0].Value is string text && text.Length > 0)
                {
                    message = text;
                }

                return true;
            }
        }
        catch (Exception exception) when (exception is TypeLoadException or FileNotFoundException or FileLoadException)
        {
        }

        return false;
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

    private static (MetadataLoadContext Context, Assembly Assembly) LoadRevitApi()
    {
        var revit = Path.Combine(AppContext.BaseDirectory, "revit-ref", "RevitAPI.dll");
        var runtime = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var paths = Directory.GetFiles(runtime, "*.dll").Append(revit);
        var context = new MetadataLoadContext(new PathAssemblyResolver(paths));
        return (context, context.LoadFromAssemblyPath(revit));
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
