using System.Text.Json;

namespace BimIt.Revit.Server;

internal static class GetSelectionJsonSchemas
{
    public static JsonElement Input { get; } = Parse(
        """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "instance_id": { "type": ["string", "null"] },
            "document_id": { "type": ["string", "null"] },
            "limit": {
              "type": "integer",
              "default": 25,
              "minimum": 1,
              "maximum": 100
            }
          }
        }
        """);

    public static JsonElement Output { get; } = Parse(
        """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["context", "selected_count", "truncated", "element_refs"],
          "properties": {
            "context": {
              "type": "object",
              "additionalProperties": false,
              "required": ["instance_id", "document_id"],
              "properties": {
                "instance_id": { "type": "string" },
                "document_id": { "type": "string" }
              }
            },
            "selected_count": { "type": "integer", "minimum": 0 },
            "truncated": { "type": "boolean" },
            "element_refs": {
              "type": "array",
              "maxItems": 100,
              "items": { "type": "string" }
            }
          }
        }
        """);

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
