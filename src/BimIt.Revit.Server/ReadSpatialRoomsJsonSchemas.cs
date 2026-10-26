using System.Text.Json;

namespace BimIt.Revit.Server;

internal static class ReadSpatialRoomsJsonSchemas
{
    public static JsonElement Input { get; } = Parse(
        """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["document_id"],
          "properties": {
            "instance_id": { "type": ["string", "null"] },
            "document_id": { "type": "string", "minLength": 1, "maxLength": 128 },
            "name_contains": { "type": ["string", "null"], "minLength": 1, "maxLength": 40 },
            "limit": { "type": ["integer", "null"], "minimum": 1, "maximum": 100 }
          }
        }
        """);

    public static JsonElement Output { get; } = Parse(
        """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["context", "length_unit", "chord_tolerance_m", "truncated", "links", "items"],
          "properties": {
            "context": { "type": "object" },
            "length_unit": { "type": "string" },
            "chord_tolerance_m": { "type": "number" },
            "truncated": { "type": "boolean" },
            "links": { "type": "array" },
            "items": { "type": "array" }
          }
        }
        """);

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
