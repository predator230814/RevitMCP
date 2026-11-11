using System.Text.Json;
using Xunit;

namespace BimIt.Revit.Server.Tests;

public sealed class ClosedSchemaArgumentValidatorTests
{
    [Fact]
    public void Legacy_argument_validation_stays_instance_validation()
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
        var arguments = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["limit"] = Parse("10"),
            ["note"] = Parse("""{"any":true}"""),
            ["extra"] = Parse("1")
        };

        Assert.False(ClosedSchemaArgumentValidator.IsInvalid(arguments, schema));
        Assert.False(BimIt.Revit.Contracts.JsonSchemaSubset.TryValidateDefinition(schema, out _));
    }

    [Fact]
    public void Legacy_nullable_enum_and_const_still_accept_null()
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
        var arguments = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["value"] = Parse("null")
        };

        Assert.False(ClosedSchemaArgumentValidator.IsInvalid(arguments, schema));
    }

    [Fact]
    public void Closed_objects_still_reject_undeclared_arguments()
    {
        var schema = Parse(
            """
            {
              "type": "object",
              "additionalProperties": false,
              "required": ["document_id"],
              "properties": {
                "document_id": { "type": "string", "minLength": 1 }
              }
            }
            """);

        Assert.True(ClosedSchemaArgumentValidator.IsInvalid(
            new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["document_id"] = Parse("\"doc\""),
                ["extra"] = Parse("1")
            },
            schema));
        Assert.True(ClosedSchemaArgumentValidator.HasUnexpectedProperties(
            new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["document_id"] = Parse("\"doc\""),
                ["extra"] = Parse("1")
            },
            schema));
    }

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
