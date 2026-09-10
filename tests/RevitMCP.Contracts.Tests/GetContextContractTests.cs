using System.Reflection;
using System.Text.Json;
using Xunit;

namespace RevitMCP.Contracts.Tests;

public sealed class GetContextContractTests
{
    private static readonly string[] ForbiddenPropertyFragments =
    [
        "path",
        "username",
        "user_name",
        "cloud_project",
        "cloud_region",
        "element_ids",
        "selected",
        "properties",
        "diagnostics",
        "stack",
        "extension"
    ];

    [Fact]
    public void GetContextRequest_contains_no_contract_fields()
    {
        Assert.Empty(typeof(GetContextRequest).GetProperties(BindingFlags.Instance | BindingFlags.Public));

        var json = JsonSerializer.Serialize(new GetContextRequest(), ContractJson.Options);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
        Assert.Empty(document.RootElement.EnumerateObject());
    }

    [Fact]
    public void Serialization_uses_snake_case_field_names()
    {
        var json = JsonSerializer.Serialize(CreateProjectResult(), ContractJson.Options);

        Assert.Contains("\"instance_id\"", json, StringComparison.Ordinal);
        Assert.Contains("\"revit_version\"", json, StringComparison.Ordinal);
        Assert.Contains("\"revit_build\"", json, StringComparison.Ordinal);
        Assert.Contains("\"is_workshared\"", json, StringComparison.Ordinal);
        Assert.Contains("\"is_model_in_cloud\"", json, StringComparison.Ordinal);
        Assert.Contains("\"is_read_only\"", json, StringComparison.Ordinal);
        Assert.Contains("\"is_modified\"", json, StringComparison.Ordinal);
        Assert.Contains("\"element_id\"", json, StringComparison.Ordinal);
        Assert.Contains("\"view_type\"", json, StringComparison.Ordinal);
        Assert.Contains("\"active_view\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("InstanceId", json, StringComparison.Ordinal);
        Assert.DoesNotContain("ActiveView", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Project_kind_serializes_as_project()
    {
        var json = JsonSerializer.Serialize(CreateProjectResult(), ContractJson.Options);
        Assert.Contains("\"kind\": \"project\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"kind\": \"family\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Family_kind_serializes_as_family()
    {
        var result = CreateProjectResult();
        result = new GetContextResult
        {
            Instance = result.Instance,
            Document = new GetContextDocument
            {
                Title = "Family1",
                Kind = GetContextDocumentKind.Family,
                IsWorkshared = false,
                IsModelInCloud = false,
                IsReadOnly = false,
                IsModified = false
            },
            ActiveView = result.ActiveView,
            Selection = result.Selection
        };

        var json = JsonSerializer.Serialize(result, ContractJson.Options);
        Assert.Contains("\"kind\": \"family\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Element_id_is_a_string()
    {
        var json = JsonSerializer.Serialize(CreateProjectResult(), ContractJson.Options);
        using var document = JsonDocument.Parse(json);
        var elementId = document.RootElement.GetProperty("active_view").GetProperty("element_id");
        Assert.Equal(JsonValueKind.String, elementId.ValueKind);
        Assert.Equal("184392", elementId.GetString());
    }

    [Fact]
    public void Zero_document_output_contains_explicit_null_document_and_active_view()
    {
        var json = JsonSerializer.Serialize(CreateZeroDocumentResult(), ContractJson.Options);
        using var document = JsonDocument.Parse(json);

        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("document").ValueKind);
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("active_view").ValueKind);
        Assert.Contains("\"document\": null", json, StringComparison.Ordinal);
        Assert.Contains("\"active_view\": null", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Selection_always_contains_only_count()
    {
        foreach (var result in new[] { CreateZeroDocumentResult(), CreateProjectResult() })
        {
            var json = JsonSerializer.Serialize(result, ContractJson.Options);
            using var document = JsonDocument.Parse(json);
            var selection = document.RootElement.GetProperty("selection");
            Assert.Equal(["count"], selection.EnumerateObject().Select(property => property.Name).ToArray());
            Assert.True(selection.GetProperty("count").GetInt32() >= 0);
        }

        Assert.Equal(
            [nameof(GetContextSelection.Count)],
            typeof(GetContextSelection).GetProperties().Select(property => property.Name).ToArray());
    }

    [Fact]
    public void Result_contract_has_no_selected_ids_path_user_or_cloud_fields()
    {
        var names = CollectPublicPropertyNames(
            typeof(GetContextRequest),
            typeof(GetContextResult),
            typeof(GetContextInstance),
            typeof(GetContextDocument),
            typeof(GetContextActiveView),
            typeof(GetContextSelection));

        foreach (var fragment in ForbiddenPropertyFragments)
        {
            Assert.DoesNotContain(names, name => name.Contains(fragment, StringComparison.OrdinalIgnoreCase));
        }

        var json = JsonSerializer.Serialize(CreateProjectResult(), ContractJson.Options)
            + JsonSerializer.Serialize(CreateZeroDocumentResult(), ContractJson.Options);
        Assert.DoesNotContain("element_ids", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("file_path", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("username", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cloud_project", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cloud_region", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Result_contract_has_no_generic_extension_or_property_bag()
    {
        var types = new[]
        {
            typeof(GetContextRequest),
            typeof(GetContextResult),
            typeof(GetContextInstance),
            typeof(GetContextDocument),
            typeof(GetContextActiveView),
            typeof(GetContextSelection)
        };

        foreach (var type in types)
        {
            foreach (var property in type.GetProperties())
            {
                Assert.False(IsPropertyBag(property.PropertyType), $"{type.Name}.{property.Name} is a property bag.");
            }
        }
    }

    [Fact]
    public void Accepted_result_shape_is_exactly_the_cap0001_fields()
    {
        Assert.Equal(
            [nameof(GetContextResult.ActiveView), nameof(GetContextResult.Document), nameof(GetContextResult.Instance), nameof(GetContextResult.Selection)],
            typeof(GetContextResult).GetProperties().Select(property => property.Name).OrderBy(name => name).ToArray());
        Assert.Equal(
            [nameof(GetContextInstance.InstanceId), nameof(GetContextInstance.RevitBuild), nameof(GetContextInstance.RevitVersion)],
            typeof(GetContextInstance).GetProperties().Select(property => property.Name).OrderBy(name => name).ToArray());
        Assert.Equal(
            [
                nameof(GetContextDocument.IsModelInCloud),
                nameof(GetContextDocument.IsModified),
                nameof(GetContextDocument.IsReadOnly),
                nameof(GetContextDocument.IsWorkshared),
                nameof(GetContextDocument.Kind),
                nameof(GetContextDocument.Title)
            ],
            typeof(GetContextDocument).GetProperties().Select(property => property.Name).OrderBy(name => name).ToArray());
        Assert.Equal(
            [nameof(GetContextActiveView.ElementId), nameof(GetContextActiveView.Name), nameof(GetContextActiveView.ViewType)],
            typeof(GetContextActiveView).GetProperties().Select(property => property.Name).OrderBy(name => name).ToArray());
    }

    [Fact]
    public void Round_trip_preserves_defined_contract()
    {
        var original = CreateProjectResult();
        var json = JsonSerializer.Serialize(original, ContractJson.Options);
        var restored = JsonSerializer.Deserialize<GetContextResult>(json, ContractJson.Options);

        Assert.NotNull(restored);
        Assert.Equal(original.Instance.InstanceId, restored.Instance.InstanceId);
        Assert.Equal(original.Instance.RevitVersion, restored.Instance.RevitVersion);
        Assert.Equal(original.Instance.RevitBuild, restored.Instance.RevitBuild);
        Assert.Equal(original.Document!.Title, restored.Document!.Title);
        Assert.Equal(original.Document.Kind, restored.Document.Kind);
        Assert.Equal(original.Document.IsWorkshared, restored.Document.IsWorkshared);
        Assert.Equal(original.Document.IsModelInCloud, restored.Document.IsModelInCloud);
        Assert.Equal(original.Document.IsReadOnly, restored.Document.IsReadOnly);
        Assert.Equal(original.Document.IsModified, restored.Document.IsModified);
        Assert.Equal(original.ActiveView!.ElementId, restored.ActiveView!.ElementId);
        Assert.Equal(original.ActiveView.Name, restored.ActiveView.Name);
        Assert.Equal(original.ActiveView.ViewType, restored.ActiveView.ViewType);
        Assert.Equal(original.Selection.Count, restored.Selection.Count);

        var zero = JsonSerializer.Deserialize<GetContextResult>(
            JsonSerializer.Serialize(CreateZeroDocumentResult(), ContractJson.Options),
            ContractJson.Options);
        Assert.NotNull(zero);
        Assert.Null(zero.Document);
        Assert.Null(zero.ActiveView);
        Assert.Equal(0, zero.Selection.Count);
    }

    private static GetContextResult CreateZeroDocumentResult()
    {
        return new GetContextResult
        {
            Instance = new GetContextInstance
            {
                InstanceId = "a81e3fd0-6d57-4d27-8dbc-b2f9e98221ac",
                RevitVersion = "2026",
                RevitBuild = "26.5.0.55"
            },
            Document = null,
            ActiveView = null,
            Selection = new GetContextSelection { Count = 0 }
        };
    }

    private static GetContextResult CreateProjectResult()
    {
        return new GetContextResult
        {
            Instance = new GetContextInstance
            {
                InstanceId = "a81e3fd0-6d57-4d27-8dbc-b2f9e98221ac",
                RevitVersion = "2026",
                RevitBuild = "26.5.0.55"
            },
            Document = new GetContextDocument
            {
                Title = "Hospital-MEP",
                Kind = GetContextDocumentKind.Project,
                IsWorkshared = true,
                IsModelInCloud = true,
                IsReadOnly = false,
                IsModified = true
            },
            ActiveView = new GetContextActiveView
            {
                ElementId = "184392",
                Name = "Level 02 - HVAC",
                ViewType = "FloorPlan"
            },
            Selection = new GetContextSelection { Count = 12 }
        };
    }

    private static string[] CollectPublicPropertyNames(params Type[] types)
    {
        return types
            .SelectMany(type => type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            .Select(property => property.Name)
            .ToArray();
    }

    private static bool IsPropertyBag(Type type)
    {
        if (type == typeof(string) || type.IsPrimitive || type.IsEnum)
        {
            return false;
        }

        if (type.IsGenericType)
        {
            var definition = type.GetGenericTypeDefinition();
            if (definition == typeof(Dictionary<,>)
                || definition == typeof(IDictionary<,>)
                || definition == typeof(IReadOnlyDictionary<,>))
            {
                return true;
            }
        }

        return type == typeof(object)
            || type == typeof(JsonElement)
            || type == typeof(JsonElement?);
    }
}
