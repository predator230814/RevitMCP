using System.Text.Json;
using Xunit;

namespace BimIt.Revit.Contracts.Tests;

public sealed class JsonSchemaSubsetTests
{
    [Fact]
    public void Instance_validation_accepts_nullable_closed_objects_and_constraints()
    {
        var schema = Parse(
            """
            {
              "type": "object",
              "additionalProperties": false,
              "required": ["name"],
              "properties": {
                "name": { "type": "string", "minLength": 1, "maxLength": 3, "enum": ["a", "ab"], "const": "ab" },
                "count": { "type": ["integer", "null"], "minimum": 1, "maximum": 2 },
                "tags": {
                  "type": "array",
                  "minItems": 1,
                  "maxItems": 2,
                  "uniqueItems": true,
                  "items": { "type": "string" }
                },
                "child": {
                  "type": "object",
                  "additionalProperties": false,
                  "properties": { "flag": { "type": "boolean" } }
                }
              }
            }
            """);

        Assert.True(JsonSchemaSubset.MatchesArguments(Object("""{"name":"ab","count":null,"tags":["x"],"child":{"flag":true}}"""), schema));
        Assert.False(JsonSchemaSubset.MatchesArguments(Object("""{"name":"ab","count":3}"""), schema));
        Assert.False(JsonSchemaSubset.MatchesArguments(Object("""{"count":1}"""), schema));
        Assert.False(JsonSchemaSubset.MatchesArguments(Object("""{"name":"ab","extra":1}"""), schema));
        Assert.False(JsonSchemaSubset.MatchesArguments(Object("""{"name":"ab","tags":["x","x"]}"""), schema));
        Assert.False(JsonSchemaSubset.MatchesInstance(Parse("1.5"), Parse("""{"type":"integer"}""")));
        Assert.True(JsonSchemaSubset.MatchesInstance(Parse("1.5"), Parse("""{"type":"number","minimum":1,"maximum":2}""")));
    }

    [Fact]
    public void Strict_instance_validation_applies_enum_and_const_to_null()
    {
        var enumSchema = Parse("""{"type":["string","null"],"enum":["a"]}""");
        Assert.False(JsonSchemaSubset.MatchesInstance(Parse("null"), enumSchema));
        Assert.True(JsonSchemaSubset.MatchesInstance(Parse("\"a\""), enumSchema));

        var enumWithNull = Parse("""{"type":["string","null"],"enum":["a",null]}""");
        Assert.True(JsonSchemaSubset.MatchesInstance(Parse("null"), enumWithNull));

        var constSchema = Parse("""{"type":["string","null"],"const":"a"}""");
        Assert.False(JsonSchemaSubset.MatchesInstance(Parse("null"), constSchema));

        var constNull = Parse("""{"type":["string","null"],"const":null}""");
        Assert.True(JsonSchemaSubset.MatchesInstance(Parse("null"), constNull));
    }

    [Fact]
    public void Strict_null_enum_and_const_apply_inside_objects_and_arrays()
    {
        var nested = Parse(
            """
            {
              "type": "object",
              "additionalProperties": false,
              "properties": {
                "value": { "type": ["string", "null"], "enum": ["a"] }
              }
            }
            """);
        Assert.False(JsonSchemaSubset.MatchesInstance(Parse("""{"value":null}"""), nested));
        Assert.True(JsonSchemaSubset.MatchesInstance(Parse("""{"value":"a"}"""), nested));

        var items = Parse(
            """
            {
              "type": "array",
              "items": { "type": ["string", "null"], "const": "a" }
            }
            """);
        Assert.False(JsonSchemaSubset.MatchesInstance(Parse("""[null]"""), items));
        Assert.True(JsonSchemaSubset.MatchesInstance(Parse("""["a"]"""), items));
    }

    [Fact]
    public void Legacy_argument_validation_still_accepts_null_before_enum_and_const()
    {
        var schema = Parse(
            """
            {
              "type": "object",
              "properties": {
                "value": { "type": ["string", "null"], "enum": ["a"], "const": "a" }
              }
            }
            """);

        Assert.True(JsonSchemaSubset.MatchesArguments(Object("""{"value":null}"""), schema));
        Assert.False(JsonSchemaSubset.MatchesInstance(Parse("""{"value":null}"""), schema));
        Assert.False(JsonSchemaSubset.TryMatchInstance(Parse("""{"value":null}"""), schema, out var diagnostic));
        Assert.Equal("$.value", diagnostic.Path);
        Assert.Equal("enum", diagnostic.Keyword);
    }

    [Fact]
    public void Strict_instance_diagnostics_name_the_path_and_keyword()
    {
        var schema = """
            {
              "type": "object",
              "additionalProperties": false,
              "required": ["name"],
              "properties": {
                "name": { "type": "string", "minLength": 1, "maxLength": 4 },
                "code": { "type": "string", "enum": ["a", "ab"] },
                "count": { "type": "integer", "minimum": 1, "maximum": 2 },
                "child": {
                  "type": "object",
                  "additionalProperties": false,
                  "properties": {
                    "label": { "type": "string", "minLength": 1 }
                  }
                },
                "rows": {
                  "type": "array",
                  "items": {
                    "type": "object",
                    "additionalProperties": false,
                    "required": ["name"],
                    "properties": { "name": { "type": "string" } }
                  }
                }
              }
            }
            """;

        AssertDiagnostic("""{"extra":"secret-value"}""", schema, "$.extra", "additionalProperties");
        AssertDiagnostic("""{"name":""}""", schema, "$.name", "minLength");
        AssertDiagnostic("""{"name":"abcde"}""", schema, "$.name", "maxLength");
        AssertDiagnostic("""{"name":1}""", schema, "$.name", "type");
        AssertDiagnostic("""{"name":"a","count":0}""", schema, "$.count", "minimum");
        AssertDiagnostic("""{"name":"a","count":3}""", schema, "$.count", "maximum");
        AssertDiagnostic("""{"name":"a","code":"zz"}""", schema, "$.code", "enum");
        AssertDiagnostic("""{"name":"a","child":{"label":""}}""", schema, "$.child.label", "minLength");
        AssertDiagnostic("""{"name":"a","rows":[{"name":1}]}""", schema, "$.rows[0].name", "type");
        AssertDiagnostic("""{"name":"a","rows":[{}]}""", schema, "$.rows[0].name", "required");

        var accepted = Parse("""{"name":"ab","count":1,"child":{"label":"ok"},"rows":[{"name":"n"}]}""");
        Assert.True(JsonSchemaSubset.MatchesInstance(accepted, Parse(schema)));
        Assert.True(JsonSchemaSubset.TryMatchInstance(accepted, Parse(schema), out var none));
        Assert.Null(none);

        var enumSchema = Parse("""{"type":["string","null"],"enum":["a"]}""");
        Assert.False(JsonSchemaSubset.TryMatchInstance(Parse("null"), enumSchema, out var enumError));
        Assert.Equal("$", enumError.Path);
        Assert.Equal("enum", enumError.Keyword);
        Assert.False(JsonSchemaSubset.MatchesInstance(Parse("null"), enumSchema));
        Assert.True(JsonSchemaSubset.MatchesInstance(Parse("\"a\""), enumSchema));

        var constSchema = Parse("""{"type":["string","null"],"const":"a"}""");
        Assert.False(JsonSchemaSubset.TryMatchInstance(Parse("null"), constSchema, out var constError));
        Assert.Equal("const", constError.Keyword);
        Assert.True(JsonSchemaSubset.MatchesInstance(Parse("null"), Parse("""{"type":["string","null"],"const":null}""")));
        Assert.True(JsonSchemaSubset.MatchesArguments(Object("""{"value":null}"""), Parse("""{"type":"object","properties":{"value":{"type":["string","null"],"enum":["a"],"const":"a"}}}""")));
    }

    [Fact]
    public void Instance_validation_ignores_unknown_keywords_and_missing_type()
    {
        var schema = Parse(
            """
            {
              "type": "object",
              "properties": {
                "limit": { "type": "integer", "default": 25, "minimum": 1, "pattern": "^[0-9]+$" },
                "note": { "description": "legacy annotation without a type" }
              }
            }
            """);

        Assert.True(JsonSchemaSubset.MatchesArguments(Object("""{"limit":10,"note":{"any":true},"extra":1}"""), schema));
        Assert.False(JsonSchemaSubset.TryValidateDefinition(schema, out var error));
        Assert.Equal("pattern", error.Keyword);
    }

    [Theory]
    [InlineData("""{"type":"object","additionalProperties":false}""")]
    [InlineData("""{"type":"object","additionalProperties":false,"properties":{"child":{"type":"object","additionalProperties":false}}}""")]
    [InlineData("""{"type":"array","items":{"type":"string","minLength":1,"maxLength":4}}""")]
    [InlineData("""{"type":["object","null"],"additionalProperties":false}""")]
    [InlineData("""{"type":"string","enum":["a","b"]}""")]
    [InlineData("""{"type":"string","description":"text","title":"Text","default":"a","minLength":0,"maxLength":1}""")]
    [InlineData("""{"type":"integer","minimum":1,"maximum":1}""")]
    public void Definition_validation_accepts_closed_subset_schemas(string json)
    {
        Assert.True(JsonSchemaSubset.TryValidateDefinition(Parse(json), out var error), error?.Message);
        Assert.Null(error);
    }

    [Fact]
    public void Definition_validation_rejects_a_missing_root_type()
    {
        var error = AssertInvalid("{}", "type");
        Assert.Equal("$", error.Path);
    }

    [Fact]
    public void Definition_validation_rejects_an_untyped_nested_property()
    {
        var error = AssertInvalid(
            """{"type":"object","additionalProperties":false,"properties":{"some_value":{}}}""",
            "type");
        Assert.Equal("$/properties/some_value", error.Path);
    }

    [Fact]
    public void Definition_validation_rejects_enum_without_type_and_accepts_enum_with_type()
    {
        var error = AssertInvalid("""{"enum":["a","b"]}""", "type");
        Assert.Equal("$", error.Path);
        Assert.True(JsonSchemaSubset.TryValidateDefinition(Parse("""{"type":"string","enum":["a","b"]}"""), out _));
    }

    [Theory]
    [InlineData("""{"type":"date"}""", "type")]
    [InlineData("""{"type":[]}""", "type")]
    [InlineData("""{"type":["string","string"]}""", "type")]
    [InlineData("""{"type":"object"}""", "additionalProperties")]
    [InlineData("""{"type":["object","null"]}""", "additionalProperties")]
    [InlineData("""{"type":"object","additionalProperties":true}""", "additionalProperties")]
    [InlineData("""{"type":"object","additionalProperties":{"type":"string"}}""", "additionalProperties")]
    [InlineData("""{"type":"object","additionalProperties":false,"properties":[]}""", "properties")]
    [InlineData("""{"type":"object","additionalProperties":false,"properties":{"a":1}}""", "properties")]
    [InlineData("""{"type":"object","additionalProperties":false,"properties":{"a":{"type":"string"}},"required":["a","a"]}""", "required")]
    [InlineData("""{"type":"object","additionalProperties":false,"properties":{"a":{"type":"string"}},"required":["b"]}""", "required")]
    [InlineData("""{"type":"array","items":[{"type":"string"}]}""", "items")]
    [InlineData("""{"type":"array","items":"string"}""", "items")]
    [InlineData("""{"type":"string","minLength":-1}""", "minLength")]
    [InlineData("""{"type":"string","minLength":3,"maxLength":1}""", "minLength")]
    [InlineData("""{"type":"array","minItems":-1,"items":{"type":"string"}}""", "minItems")]
    [InlineData("""{"type":"integer","minimum":2,"maximum":1}""", "minimum")]
    [InlineData("""{"type":"array","items":{"type":"string"},"uniqueItems":"yes"}""", "uniqueItems")]
    [InlineData("""{"type":"string","enum":{"a":1}}""", "enum")]
    [InlineData("""{"type":"string","description":1}""", "description")]
    [InlineData("""{"type":"string","title":1}""", "title")]
    [InlineData("""{"type":"string","properties":{}}""", "properties")]
    [InlineData("""{"type":"string","minItems":1}""", "minItems")]
    [InlineData("""{"type":"number","minLength":1}""", "minLength")]
    [InlineData("""{"type":"boolean","minimum":0}""", "minimum")]
    [InlineData("""{"type":"string","pattern":"a"}""", "pattern")]
    [InlineData("""{"type":"string","oneOf":[{"type":"string"}]}""", "oneOf")]
    public void Definition_validation_rejects_malformed_operation_schemas(string json, string keyword)
    {
        AssertInvalid(json, keyword);
    }

    [Fact]
    public void Operation_schema_root_must_be_a_plain_object_type()
    {
        Assert.True(JsonSchemaSubset.TryValidateOperationSchema(
            Parse("""{"type":"object","additionalProperties":false}"""),
            out _));
        var error = AssertOperationInvalid("""{"type":["object","null"],"additionalProperties":false}""");
        Assert.Equal("type", error.Keyword);
    }

    private static JsonSchemaDefinitionError AssertInvalid(string json, string keyword)
    {
        var schema = Parse(json);
        Assert.False(JsonSchemaSubset.TryValidateDefinition(schema, out var error));
        Assert.NotNull(error);
        Assert.Equal(keyword, error.Keyword);
        Assert.False(string.IsNullOrWhiteSpace(error.Path));
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
        return error;
    }

    private static JsonSchemaDefinitionError AssertOperationInvalid(string json)
    {
        Assert.False(JsonSchemaSubset.TryValidateOperationSchema(Parse(json), out var error));
        Assert.NotNull(error);
        return error;
    }

    private static void AssertDiagnostic(string instance, string schema, string path, string keyword)
    {
        var parsedSchema = Parse(schema);
        var parsedInstance = Parse(instance);
        Assert.False(JsonSchemaSubset.MatchesInstance(parsedInstance, parsedSchema));
        Assert.False(JsonSchemaSubset.TryMatchInstance(parsedInstance, parsedSchema, out var error));
        Assert.NotNull(error);
        Assert.Equal(path, error.Path);
        Assert.Equal(keyword, error.Keyword);
        Assert.DoesNotContain("secret-value", error.Path, StringComparison.Ordinal);
    }

    private static IDictionary<string, JsonElement> Object(string json)
    {
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in Parse(json).EnumerateObject())
        {
            result.Add(property.Name, property.Value.Clone());
        }

        return result;
    }

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
