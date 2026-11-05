using BimIt.Revit.Addin.Identity;
using BimIt.Revit.Addin.Inspection;
using BimIt.Revit.Contracts;
using Xunit;

namespace BimIt.Revit.Addin.Tests;

/// <summary>
/// CAP-0015 "Definition classification": the definition-based entry points share the
/// CAP-0004 rules of the Parameter path. Revit API objects are not instantiated here.
/// The pure decisions are tested directly, and the Revit adapter is checked structurally.
/// Live Parameter-versus-Definition parity remains a CAP-0015 integration validation.
/// </summary>
public sealed class DefinitionParameterClassificationTests
{
    private static readonly Guid Shared = Guid.Parse("AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE");

    [Fact]
    public void Built_in_still_takes_priority_over_shared_and_local()
    {
        var definition = Definition(
            "autodesk.revit.parameter:allModelMark",
            Shared,
            "local:12");
        var parameter = ParameterIdentityClassifier.Classify(
            isBuiltIn: true,
            builtInTypeId: "autodesk.revit.parameter:allModelMark",
            isShared: true,
            sharedGuid: Shared,
            localStableKey: "local:12");

        Assert.Equal(DescribeParameterIdentityKind.BuiltIn, definition.Kind);
        Assert.Equal("autodesk.revit.parameter:allModelMark", definition.ParameterTypeId);
        Assert.Null(definition.SharedGuid);
        Assert.Equal(parameter, definition);
    }

    [Fact]
    public void Shared_guid_canonicalization_is_unchanged()
    {
        var definition = Definition(null, Shared, "local:12");
        var parameter = ParameterIdentityClassifier.Classify(false, null, true, Shared, "local:12");

        Assert.Equal(DescribeParameterIdentityKind.Shared, definition.Kind);
        Assert.Equal("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", definition.SharedGuid);
        Assert.Equal("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", definition.StableKey);
        Assert.Null(definition.ParameterTypeId);
        Assert.Equal(parameter, definition);
    }

    [Fact]
    public void A_shared_parameter_element_with_an_empty_guid_falls_through_to_local_as_before()
    {
        var definition = Definition(null, Guid.Empty, "uid-1");
        var parameter = ParameterIdentityClassifier.Classify(false, null, true, Guid.Empty, "uid-1");

        Assert.Equal(DescribeParameterIdentityKind.Local, definition.Kind);
        Assert.Equal(parameter, definition);
    }

    [Fact]
    public void Local_stable_key_rule_is_unchanged()
    {
        Assert.Equal("4c1b-uid", ParameterIdentityClassifier.LocalStableKey("4c1b-uid", 12));
        Assert.Equal("local:12", ParameterIdentityClassifier.LocalStableKey(null, 12));
        Assert.Equal("local:12", ParameterIdentityClassifier.LocalStableKey("", 12));
        Assert.Equal("local:-1005", ParameterIdentityClassifier.LocalStableKey(null, -1005));
        Assert.Equal("local:9007199254740993", ParameterIdentityClassifier.LocalStableKey(null, 9007199254740993));

        var key = ParameterIdentityClassifier.LocalStableKey(null, 12);
        var definition = Definition(null, null, key);
        var parameter = ParameterIdentityClassifier.Classify(false, null, false, Guid.Empty, key);
        Assert.Equal(DescribeParameterIdentityKind.Local, definition.Kind);
        Assert.Equal("local:12", definition.StableKey);
        Assert.Equal(parameter, definition);
    }

    [Fact]
    public void A_null_or_empty_type_id_never_reaches_the_built_in_check()
    {
        static bool Unreachable() => throw new InvalidOperationException("IsBuiltInParameter must not be called.");

        Assert.False(ParameterIdentityClassifier.IsBuiltIn(null, Unreachable));
        Assert.False(ParameterIdentityClassifier.IsBuiltIn(string.Empty, Unreachable));
        Assert.False(ParameterIdentityClassifier.IsBuiltIn("autodesk.revit.parameter:custom", () => false));
        Assert.True(ParameterIdentityClassifier.IsBuiltIn("autodesk.revit.parameter:allModelMark", () => true));

        var calls = 0;
        ParameterIdentityClassifier.IsBuiltIn("autodesk.revit.parameter:allModelMark", () =>
        {
            calls++;
            return true;
        });
        Assert.Equal(1, calls);

        // A definition with no type id falls through to shared, then local.
        Assert.Equal(DescribeParameterIdentityKind.Shared, Definition(null, Shared, "uid").Kind);
        Assert.Equal(DescribeParameterIdentityKind.Local, Definition(null, null, "uid").Kind);

        var adapter = Adapter();
        Assert.Contains("typeId is null || typeId.Empty() ? null : typeId.TypeId", adapter, StringComparison.Ordinal);
        Assert.Contains("ParameterIdentityClassifier.IsBuiltIn(text, () => ParameterUtils.IsBuiltInParameter(typeId!))", adapter, StringComparison.Ordinal);
        Assert.Equal(1, Count(adapter, "ParameterUtils.IsBuiltInParameter("));
        Assert.Contains("var builtInTypeId = BuiltInTypeId(typeId);", adapter, StringComparison.Ordinal);
        Assert.Contains("var builtInTypeId = BuiltInTypeId(definition.GetParameterTypeId());", adapter, StringComparison.Ordinal);
    }

    [Fact]
    public void The_definition_path_reads_the_shared_guid_from_the_shared_parameter_element()
    {
        var body = Slice(Adapter(), "Classify(InternalDefinition definition, Document document)", "public static DescribeParameterDataType");
        Assert.Contains("BuiltInTypeId(definition.GetParameterTypeId())", body, StringComparison.Ordinal);
        Assert.Contains("document.GetElement(definition.Id)", body, StringComparison.Ordinal);
        Assert.Contains("definitionElement is SharedParameterElement shared ? shared.GuidValue : null", body, StringComparison.Ordinal);
        Assert.Contains("ResolveLocalKey(definition, definitionElement)", body, StringComparison.Ordinal);
        Assert.Equal(1, Count(body, "GetElement("));

        // The element lookup lives only inside the resolver passed to ClassifyDefinition,
        // which skips it for a built-in definition.
        var classify = body.IndexOf("ParameterIdentityClassifier.ClassifyDefinition(", StringComparison.Ordinal);
        var resolver = body.IndexOf("() =>", StringComparison.Ordinal);
        var lookup = body.IndexOf("document.GetElement(definition.Id)", StringComparison.Ordinal);
        Assert.True(classify >= 0 && classify < resolver && resolver < lookup);
    }

    [Fact]
    public void A_built_in_definition_is_classified_without_looking_up_its_element()
    {
        var identity = ParameterIdentityClassifier.ClassifyDefinition(
            "autodesk.revit.parameter:allModelMark",
            () => throw new InvalidOperationException("Document.GetElement must not be called for a built-in definition."));
        Assert.Equal(DescribeParameterIdentityKind.BuiltIn, identity.Kind);
        Assert.Equal("autodesk.revit.parameter:allModelMark", identity.ParameterTypeId);
        Assert.Equal("autodesk.revit.parameter:allModelMark", identity.StableKey);
        Assert.Null(identity.SharedGuid);

        foreach (var typeId in new string?[] { null, string.Empty })
        {
            var lookups = 0;
            var shared = ParameterIdentityClassifier.ClassifyDefinition(typeId, () =>
            {
                lookups++;
                return new DefinitionElementFacts(Shared, "uid");
            });
            Assert.Equal(1, lookups);
            Assert.Equal(DescribeParameterIdentityKind.Shared, shared.Kind);
        }
    }

    [Fact]
    public void Both_paths_share_one_local_stable_key_rule()
    {
        var adapter = Adapter();
        var parameterPath = Slice(adapter, "private static string ResolveLocalKey(Parameter parameter, Document document)", "private static string ResolveLocalKey(InternalDefinition");
        Assert.Contains("ResolveLocalKey(definition, document.GetElement(definition.Id))", parameterPath, StringComparison.Ordinal);
        Assert.Contains("ParameterIdentityClassifier.LocalStableKey(null, parameter.Id.Value)", parameterPath, StringComparison.Ordinal);

        var definitionRule = Slice(adapter, "private static string ResolveLocalKey(InternalDefinition definition, Element? definitionElement)", "\n}");
        Assert.Contains("(definitionElement as ParameterElement)?.UniqueId", definitionRule, StringComparison.Ordinal);
        Assert.Contains("definition.Id.Value", definitionRule, StringComparison.Ordinal);

        Assert.DoesNotContain("\"local:\"", adapter, StringComparison.Ordinal);
        Assert.Equal(2, Count(adapter, "ParameterIdentityClassifier.LocalStableKey("));
    }

    [Fact]
    public void Definition_data_type_uses_the_one_existing_classification()
    {
        var adapter = Adapter();
        var definitionPath = Slice(adapter, "public static DescribeParameterDataType ClassifyDataType(Definition definition)", "private static DescribeParameterDataType ClassifyDataType(ForgeTypeId? dataType)");
        Assert.Contains("ClassifyDataType(definition.GetDataType())", definitionPath, StringComparison.Ordinal);

        var rule = Slice(adapter, "private static DescribeParameterDataType ClassifyDataType(ForgeTypeId? dataType)", "private static string? BuiltInTypeId");
        Assert.Contains("dataType is null || dataType.Empty()", rule, StringComparison.Ordinal);
        Assert.Contains("ParameterDataTypeClassifier.Classify(null, false, false, false)", rule, StringComparison.Ordinal);
        Assert.Contains("UnitUtils.IsMeasurableSpec(dataType)", rule, StringComparison.Ordinal);
        Assert.Contains("Category.IsBuiltInCategory(dataType)", rule, StringComparison.Ordinal);
        Assert.Contains("SpecUtils.IsSpec(dataType)", rule, StringComparison.Ordinal);

        Assert.Equal(1, Count(adapter, "UnitUtils.IsMeasurableSpec("));
        Assert.Equal(1, Count(adapter, "Category.IsBuiltInCategory("));
        Assert.Equal(1, Count(adapter, "SpecUtils.IsSpec("));
        Assert.Equal(1, Count(adapter, "GetDataType()"));
        Assert.Equal(2, Count(adapter, "ParameterDataTypeClassifier.Classify("));

        // The pure rule the adapter feeds is unchanged.
        Assert.Equal(DescribeParameterDataTypeKind.Unknown, ParameterDataTypeClassifier.Classify(null, false, false, false).Kind);
        Assert.Null(ParameterDataTypeClassifier.Classify(null, false, false, false).ForgeTypeId);
        Assert.Equal(DescribeParameterDataTypeKind.MeasurableSpec, ParameterDataTypeClassifier.Classify("autodesk.spec.aec.electrical:potential-2.0.0", true, false, true).Kind);
        Assert.Equal(DescribeParameterDataTypeKind.Category, ParameterDataTypeClassifier.Classify("autodesk.revit.category.family:doors-1.0.0", false, true, false).Kind);
        Assert.Equal(DescribeParameterDataTypeKind.Spec, ParameterDataTypeClassifier.Classify("autodesk.spec:spec.string-2.0.0", false, false, true).Kind);
        var unknown = ParameterDataTypeClassifier.Classify("autodesk.spec:other-1.0.0", false, false, false);
        Assert.Equal(DescribeParameterDataTypeKind.Unknown, unknown.Kind);
        Assert.Equal("autodesk.spec:other-1.0.0", unknown.ForgeTypeId);
    }

    [Fact]
    public void The_parameter_data_type_path_delegates_to_the_definition_path()
    {
        var parameterPath = Slice(Adapter(), "public static DescribeParameterDataType ClassifyDataType(Parameter parameter)", "public static DescribeParameterDataType ClassifyDataType(Definition definition)");
        Assert.Contains("var definition = parameter.Definition;", parameterPath, StringComparison.Ordinal);
        Assert.Contains("definition is null ? ClassifyDataType((ForgeTypeId?)null) : ClassifyDataType(definition)", parameterPath, StringComparison.Ordinal);
        Assert.DoesNotContain("GetDataType", parameterPath, StringComparison.Ordinal);
        Assert.DoesNotContain("ParameterDataTypeClassifier", parameterPath, StringComparison.Ordinal);
    }

    [Fact]
    public void Parameter_names_including_accents_take_no_part_in_identity_or_data_type()
    {
        var adapter = Adapter();
        Assert.DoesNotContain(".Name", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("LookupParameter", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain(".Name", Source("Inspection", "ParameterIdentityClassifier.cs"), StringComparison.Ordinal);
        Assert.DoesNotContain(".Name", Source("Inspection", "ParameterDataTypeClassifier.cs"), StringComparison.Ordinal);

        // Same identity, whatever the display name ("Tt_Capacité_Sectionneur" or "Tt_Capacite_Sectionneur"),
        // gives the same parameter_ref in the existing map. No second namespace is created.
        var map = new ParameterIdentityMap();
        var fromDefinition = Definition(null, Shared, "unused");
        var fromParameter = ParameterIdentityClassifier.Classify(false, null, true, Shared, "unused");
        Assert.Equal(
            map.GetRef(GetElementParameterSource.Instance, fromParameter),
            map.GetRef(GetElementParameterSource.Instance, fromDefinition));
    }

    [Fact]
    public void Existing_capabilities_keep_the_parameter_entry_points()
    {
        foreach (var file in new[] { "RevitDescribeParametersService.cs", "RevitGetParameterValuesService.cs", "RevitPreviewParameterUpdatesService.cs" })
        {
            var service = Source("Capabilities", file);
            Assert.Contains("RevitParameterIdentity.Classify(parameter, document)", service, StringComparison.Ordinal);
            Assert.DoesNotContain("GetParameterTypeId", service, StringComparison.Ordinal);
            Assert.DoesNotContain("ParameterBindings", service, StringComparison.Ordinal);
        }
    }

    private static ClassifiedParameterIdentity Definition(string? builtInTypeId, Guid? sharedGuid, string localStableKey)
    {
        return ParameterIdentityClassifier.ClassifyDefinition(
            builtInTypeId,
            () => new DefinitionElementFacts(sharedGuid, localStableKey));
    }

    private static string Adapter() => Source("Inspection", "RevitParameterIdentity.cs");

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
