using System.Text.Json;
using BimIt.Revit.Addin.ReadOperations;
using BimIt.Revit.Contracts;
using Xunit;

namespace BimIt.Revit.Addin.Tests;

public sealed class ProjectUserWorksetsContractTests
{
    [Fact]
    public void Descriptor_uses_the_accepted_metadata_and_passes_operation_schema_validation()
    {
        var descriptor = new ProjectUserWorksetsReadOperation().Descriptor;

        Assert.Equal("project.user_worksets", descriptor.Id);
        Assert.Equal(1, descriptor.ContractVersion);
        Assert.Equal("User worksets", descriptor.Title);
        Assert.Equal(
            "User-created worksets of the active host project. Omits view, family, standard, and other internal worksets, and omits worksets that belong to linked documents. A project that is not workshared returns an empty inventory.",
            descriptor.Description);
        Assert.Equal("document", descriptor.Context);
        Assert.Equal("read", descriptor.Effects);
        Assert.Equal("cursor", descriptor.Pagination);
        Assert.Equal(ProjectUserWorksetsReadOperation.InputSchemaJson, descriptor.InputSchema!.Value.GetRawText());
        Assert.Equal(ProjectUserWorksetsReadOperation.OutputSchemaJson, descriptor.OutputSchema!.Value.GetRawText());
        Assert.True(JsonSchemaSubset.TryValidateOperationSchema(descriptor.InputSchema.Value, out _));
        Assert.True(JsonSchemaSubset.TryValidateOperationSchema(descriptor.OutputSchema.Value, out _));

        var registry = new ReadOperationRegistry([new ProjectUserWorksetsReadOperation()]);
        var exact = registry.LookupExact("project.user_worksets", null);
        Assert.Equal(ReadOperationResolution.Found, exact.Resolution);
        Assert.Equal(ProjectUserWorksetsReadOperation.InputSchemaJson, exact.Result!.Operations[0].InputSchema!.Value.GetRawText());
    }

    [Fact]
    public void Input_schema_accepts_only_name_contains()
    {
        using var document = JsonDocument.Parse(ProjectUserWorksetsReadOperation.InputSchemaJson);
        var root = document.RootElement;
        Assert.Equal("object", root.GetProperty("type").GetString());
        Assert.False(root.GetProperty("additionalProperties").GetBoolean());
        Assert.False(root.TryGetProperty("required", out _));
        var name = Assert.Single(root.GetProperty("properties").EnumerateObject());
        Assert.Equal("name_contains", name.Name);
        Assert.Equal("string", name.Value.GetProperty("type").GetString());
        Assert.Equal(1, name.Value.GetProperty("minLength").GetInt32());
        Assert.Equal(256, name.Value.GetProperty("maxLength").GetInt32());
        Assert.Equal(
            "OrdinalIgnoreCase substring of the workset name. Omitted matches every user workset.",
            name.Value.GetProperty("description").GetString());
    }

    [Fact]
    public void Output_schema_is_the_closed_inventory_and_has_no_identity_or_ownership_fields()
    {
        using var document = JsonDocument.Parse(ProjectUserWorksetsReadOperation.OutputSchemaJson);
        var root = document.RootElement;
        Assert.Equal(["returned_count", "summary", "worksets"], Names(root.GetProperty("properties")));
        Assert.Equal(["returned_count", "summary", "worksets"], Required(root));

        var summary = root.GetProperty("properties").GetProperty("summary");
        Assert.Equal(["matched_workset_count", "worksharing_enabled"], Names(summary.GetProperty("properties")));
        Assert.Equal(["matched_workset_count", "worksharing_enabled"], Required(summary));
        Assert.Equal("boolean", summary.GetProperty("properties").GetProperty("worksharing_enabled").GetProperty("type").GetString());
        Assert.Equal("integer", summary.GetProperty("properties").GetProperty("matched_workset_count").GetProperty("type").GetString());
        Assert.Equal(0, summary.GetProperty("properties").GetProperty("matched_workset_count").GetProperty("minimum").GetInt32());
        Assert.Equal("integer", root.GetProperty("properties").GetProperty("returned_count").GetProperty("type").GetString());

        var item = root.GetProperty("properties").GetProperty("worksets").GetProperty("items");
        Assert.Equal(["is_default", "is_visible_by_default", "name"], Names(item.GetProperty("properties")));
        Assert.Equal(["is_default", "is_visible_by_default", "name"], Required(item));
        Assert.Equal("string", item.GetProperty("properties").GetProperty("name").GetProperty("type").GetString());
        Assert.Equal("boolean", item.GetProperty("properties").GetProperty("is_default").GetProperty("type").GetString());
        Assert.Equal("boolean", item.GetProperty("properties").GetProperty("is_visible_by_default").GetProperty("type").GetString());

        var propertyNames = PropertyNames(root);
        Assert.DoesNotContain(propertyNames, name => name.Contains("id", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(propertyNames, name => name.Contains("owner", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(propertyNames, name => name.Contains("editable", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(propertyNames, name => name.Contains("open", StringComparison.OrdinalIgnoreCase));
    }

    private static string[] Names(JsonElement properties)
    {
        return properties.EnumerateObject().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray();
    }

    private static string[] Required(JsonElement schema)
    {
        return schema.GetProperty("required").EnumerateArray().Select(item => item.GetString()!).OrderBy(name => name, StringComparer.Ordinal).ToArray();
    }

    private static List<string> PropertyNames(JsonElement schema)
    {
        var names = new List<string>();
        Collect(schema, names);
        return names;
    }

    private static void Collect(JsonElement schema, List<string> names)
    {
        if (schema.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (schema.TryGetProperty("properties", out var properties))
        {
            foreach (var property in properties.EnumerateObject())
            {
                names.Add(property.Name);
                Collect(property.Value, names);
            }
        }

        if (schema.TryGetProperty("items", out var items))
        {
            Collect(items, names);
        }
    }
}
