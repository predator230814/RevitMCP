using System.Text.Json;

namespace BimIt.Revit.Server;

internal static class GetMepDeviceTypesJsonSchemas
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
            "category_tokens": {
              "type": ["array", "null"],
              "minItems": 1,
              "maxItems": 13,
              "items": { "type": "string", "minLength": 1, "maxLength": 64 }
            },
            "search": { "type": ["string", "null"], "minLength": 1, "maxLength": 80 },
            "limit": { "type": ["integer", "null"], "minimum": 1, "maximum": 80 }
          }
        }
        """);

    public static JsonElement Output { get; } = Parse(
        """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["context", "truncated", "items"],
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
            "truncated": { "type": "boolean" },
            "items": {
              "type": "array",
              "maxItems": 80,
              "items": {
                "type": "object",
                "additionalProperties": false,
                "required": ["family_type_ref", "category_token", "category_name", "family_name", "type_name", "placement_classification", "active"],
                "properties": {
                  "family_type_ref": { "type": "string" },
                  "category_token": { "type": "string" },
                  "category_name": { "type": "string" },
                  "family_name": { "type": "string" },
                  "type_name": { "type": "string" },
                  "placement_classification": { "type": "string", "enum": ["one_level_based", "one_level_based_hosted", "work_plane_based"] },
                  "active": { "type": "boolean" }
                }
              }
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
