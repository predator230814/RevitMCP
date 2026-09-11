using System.Text.Json;
using RevitMCP.Addin.Inspection;
using RevitMCP.Contracts;
using Xunit;

namespace RevitMCP.Addin.Tests;

public sealed class GetElementInspectionShaperTests
{
    [Fact]
    public void Requested_basic_fields_only_are_projected()
    {
        var item = GetElementInspectionShaper.Ok(
            Candidate(name: "VAV Box 12", category: "Mechanical Equipment", family: "VAV Box"),
            new GetElementsProjection { Fields = [GetElementField.Name, GetElementField.CategoryName] });

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(item, ContractJson.Options));
        Assert.Equal("VAV Box 12", document.RootElement.GetProperty("name").GetString());
        Assert.Equal("Mechanical Equipment", document.RootElement.GetProperty("category_name").GetString());
        Assert.False(document.RootElement.TryGetProperty("family_name", out _));
        Assert.False(document.RootElement.TryGetProperty("type_name", out _));
        Assert.False(document.RootElement.TryGetProperty("level_name", out _));
        Assert.False(document.RootElement.TryGetProperty("parameters", out _));
    }

    [Fact]
    public void Requested_unavailable_field_is_explicit_null()
    {
        var item = GetElementInspectionShaper.Ok(
            Candidate(level: null),
            new GetElementsProjection { Fields = [GetElementField.LevelName] });

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(item, ContractJson.Options));
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("level_name").ValueKind);
    }

    [Fact]
    public void Unrequested_field_is_absent()
    {
        var item = GetElementInspectionShaper.Ok(
            Candidate(name: "A", family: "B"),
            new GetElementsProjection { Fields = [GetElementField.Name] });

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(item, ContractJson.Options));
        Assert.False(document.RootElement.TryGetProperty("family_name", out _));
        Assert.False(item.FamilyName.IsRequested);
    }

    [Fact]
    public void Parameters_follow_requested_name_order()
    {
        var item = GetElementInspectionShaper.Ok(
            Candidate(
                instance:
                [
                    Param("Flow", "1"),
                    Param("Mark", "M1")
                ]),
            ParametersOnly("Mark", "Flow"));

        Assert.Equal(["Mark", "Flow"], item.Parameters!.Select(parameter => parameter.Name).ToArray());
        Assert.False(item.ParametersTruncated);
    }

    [Fact]
    public void Instance_matches_precede_type_matches_for_the_same_name()
    {
        var item = GetElementInspectionShaper.Ok(
            Candidate(
                instance: [Param("Flow", "instance")],
                typeParameters: [Param("Flow", "type")]),
            ParametersOnly("Flow"));

        Assert.Equal(
            [GetElementParameterSource.Instance, GetElementParameterSource.Type],
            item.Parameters!.Select(parameter => parameter.Source).ToArray());
        Assert.Equal(["instance", "type"], item.Parameters!.Select(parameter => parameter.ValueText!).ToArray());
    }

    [Fact]
    public void Source_local_candidate_order_is_preserved()
    {
        var item = GetElementInspectionShaper.Ok(
            Candidate(
                instance:
                [
                    Param("Mark", "second-visible"),
                    Param("Mark", "first-listed-later")
                ]),
            ParametersOnly("Mark"));

        Assert.Equal(
            ["second-visible", "first-listed-later"],
            item.Parameters!.Select(parameter => parameter.ValueText!).ToArray());
    }

    [Fact]
    public void Duplicate_visible_same_name_entries_are_preserved()
    {
        var item = GetElementInspectionShaper.Ok(
            Candidate(
                instance:
                [
                    Param("Comments", "A"),
                    Param("Comments", "A")
                ]),
            ParametersOnly("Comments"));

        Assert.Equal(2, item.Parameters!.Count);
        Assert.All(item.Parameters, parameter =>
        {
            Assert.Equal("Comments", parameter.Name);
            Assert.Equal("A", parameter.ValueText);
        });
    }

    [Fact]
    public void Parameter_matching_is_ordinal_ignore_case_and_preserves_candidate_name()
    {
        var item = GetElementInspectionShaper.Ok(
            Candidate(instance: [Param("Flow", "850 CFM")]),
            ParametersOnly("flow"));

        Assert.Equal("Flow", Assert.Single(item.Parameters!).Name);
    }

    [Fact]
    public void Parameter_matching_does_not_trim()
    {
        var item = GetElementInspectionShaper.Ok(
            Candidate(instance: [Param("Mark", "M1"), Param(" Mark ", "padded")]),
            ParametersOnly("Mark"));

        Assert.Equal("M1", Assert.Single(item.Parameters!).ValueText);
    }

    [Fact]
    public void Zero_parameter_matches_return_empty_array_and_false()
    {
        var item = GetElementInspectionShaper.Ok(
            Candidate(instance: [Param("Comments", "x")]),
            ParametersOnly("Mark"));

        Assert.Empty(item.Parameters!);
        Assert.False(item.ParametersTruncated);

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(item, ContractJson.Options));
        Assert.Equal(0, document.RootElement.GetProperty("parameters").GetArrayLength());
        Assert.False(document.RootElement.GetProperty("parameters_truncated").GetBoolean());
    }

    [Fact]
    public void Exactly_twenty_parameter_entries_are_not_truncated()
    {
        var instance = Enumerable.Range(0, 10).Select(index => Param("Mark", $"i{index}")).ToArray();
        var type = Enumerable.Range(0, 10).Select(index => Param("Mark", $"t{index}")).ToArray();
        var item = GetElementInspectionShaper.Ok(Candidate(instance: instance, typeParameters: type), ParametersOnly("Mark"));

        Assert.Equal(20, item.Parameters!.Count);
        Assert.False(item.ParametersTruncated);
        Assert.Equal("i0", item.Parameters[0].ValueText);
        Assert.Equal("t9", item.Parameters[19].ValueText);
    }

    [Fact]
    public void More_than_twenty_matches_keep_the_first_twenty_and_truncate()
    {
        var instance = Enumerable.Range(0, 12).Select(index => Param("A", $"i{index:00}")).ToArray();
        var type = Enumerable.Range(0, 12).Select(index => Param("A", $"t{index:00}")).ToArray();
        var item = GetElementInspectionShaper.Ok(Candidate(instance: instance, typeParameters: type), ParametersOnly("A"));

        Assert.Equal(20, item.Parameters!.Count);
        Assert.True(item.ParametersTruncated);
        Assert.Equal("i00", item.Parameters[0].ValueText);
        Assert.Equal("i11", item.Parameters[11].ValueText);
        Assert.Equal("t00", item.Parameters[12].ValueText);
        Assert.Equal("t07", item.Parameters[19].ValueText);
        Assert.DoesNotContain(item.Parameters, parameter => parameter.ValueText == "t08");
    }

    [Fact]
    public void Nullable_value_text_is_preserved()
    {
        var item = GetElementInspectionShaper.Ok(
            Candidate(instance: [Param("Mark", null)]),
            ParametersOnly("Mark"));

        var parameter = Assert.Single(item.Parameters!);
        Assert.Null(parameter.ValueText);
        Assert.False(parameter.ValueTruncated);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(parameter, ContractJson.Options));
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("value_text").ValueKind);
    }

    [Fact]
    public void Value_text_at_512_is_unchanged()
    {
        var value = new string('A', 512);
        Assert.Equal((value, false), ParameterValueTextBounder.Bound(value));
    }

    [Fact]
    public void Value_text_over_512_is_truncated_without_ellipsis()
    {
        var value = new string('B', 513);
        var (text, truncated) = ParameterValueTextBounder.Bound(value);
        Assert.True(truncated);
        Assert.Equal(512, text!.Length);
        Assert.Equal(new string('B', 512), text);
        Assert.DoesNotContain("...", text, StringComparison.Ordinal);
        Assert.DoesNotContain('…', text);
    }

    [Fact]
    public void Surrogate_pair_is_not_split_at_the_truncation_boundary()
    {
        var prefix = new string('C', 511);
        var value = prefix + "\uD83D\uDE00X";
        var (text, truncated) = ParameterValueTextBounder.Bound(value);

        Assert.True(truncated);
        Assert.Equal(prefix, text);
        Assert.False(char.IsHighSurrogate(text![^1]));
        Assert.DoesNotContain('\uD83D', text);
        Assert.DoesNotContain('\uDE00', text);
    }

    [Fact]
    public void Null_value_text_bounding_is_not_truncated()
    {
        Assert.Equal((null, false), ParameterValueTextBounder.Bound(null));
    }

    [Fact]
    public void Batch_shaper_preserves_request_order_and_partial_not_found()
    {
        var resolved = new Dictionary<string, ElementInspectionCandidate>(StringComparer.Ordinal)
        {
            ["ref-a"] = Candidate(elementRef: "ref-a", name: "A"),
            ["ref-c"] = Candidate(elementRef: "ref-c", name: "C")
        };

        var result = GetElementInspectionShaper.Shape(
            new GetElementsContext { InstanceId = "instance-1", DocumentId = "doc-1" },
            ["ref-b", "ref-a", "ref-c"],
            resolved,
            new GetElementsProjection { Fields = [GetElementField.Name] });

        Assert.Equal(["ref-b", "ref-a", "ref-c"], result.Elements.Select(item => item.ElementRef).ToArray());
        Assert.Equal(
            [GetElementResultStatus.NotFound, GetElementResultStatus.Ok, GetElementResultStatus.Ok],
            result.Elements.Select(item => item.Status).ToArray());

        using var missing = JsonDocument.Parse(JsonSerializer.Serialize(result.Elements[0], ContractJson.Options));
        Assert.Equal(["element_ref", "status"], missing.RootElement.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.Equal("A", result.Elements[1].Name.Value);
    }

    [Fact]
    public void Batch_ref_lookup_is_ordinal()
    {
        var resolved = new Dictionary<string, ElementInspectionCandidate>(StringComparer.Ordinal)
        {
            ["Ref-A"] = Candidate(elementRef: "Ref-A", name: "Upper")
        };

        var result = GetElementInspectionShaper.Shape(
            new GetElementsContext { InstanceId = "i", DocumentId = "d" },
            ["ref-a", "Ref-A"],
            resolved,
            new GetElementsProjection { Fields = [GetElementField.Name] });

        Assert.Equal(GetElementResultStatus.NotFound, result.Elements[0].Status);
        Assert.Equal("ref-a", result.Elements[0].ElementRef);
        Assert.Equal(GetElementResultStatus.Ok, result.Elements[1].Status);
        Assert.Equal("Ref-A", result.Elements[1].ElementRef);
    }

    [Fact]
    public void Batch_ok_item_echoes_the_requested_ref_not_the_candidate_ref()
    {
        var resolved = new Dictionary<string, ElementInspectionCandidate>(StringComparer.Ordinal)
        {
            ["requested-ref"] = Candidate(elementRef: "canonical-ref", name: "VAV")
        };

        var result = GetElementInspectionShaper.Shape(
            new GetElementsContext { InstanceId = "i", DocumentId = "d" },
            ["requested-ref"],
            resolved,
            new GetElementsProjection { Fields = [GetElementField.Name] });

        var item = Assert.Single(result.Elements);
        Assert.Equal(GetElementResultStatus.Ok, item.Status);
        Assert.Equal("requested-ref", item.ElementRef);
        Assert.NotEqual("canonical-ref", item.ElementRef);
        Assert.Equal("VAV", item.Name.Value);
    }

    private static GetElementsProjection ParametersOnly(params string[] names)
    {
        return new GetElementsProjection { ParameterNames = names };
    }

    private static ParameterInspectionCandidate Param(string name, string? value)
    {
        return new ParameterInspectionCandidate { Name = name, ValueText = value };
    }

    private static ElementInspectionCandidate Candidate(
        string elementRef = "ref-1",
        string? name = null,
        string? category = null,
        string? family = null,
        string? typeName = null,
        string? level = null,
        IReadOnlyList<ParameterInspectionCandidate>? instance = null,
        IReadOnlyList<ParameterInspectionCandidate>? typeParameters = null)
    {
        return new ElementInspectionCandidate
        {
            ElementRef = elementRef,
            Name = name,
            CategoryName = category,
            FamilyName = family,
            TypeName = typeName,
            LevelName = level,
            InstanceParameters = instance ?? [],
            TypeParameters = typeParameters ?? []
        };
    }
}
