using System.Text.Json;

namespace BimIt.Revit.Server;

internal static class QueryFamilyTypesJsonSchemas
{
    public static JsonElement Input { get; } = Parse(
        """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["document_id"],
          "properties": {
            "instance_id": { "type": ["string", "null"] },
            "document_id": { "type": "string" },
            "category_tokens": {
              "type": ["array", "null"],
              "minItems": 1,
              "maxItems": 20,
              "items": { "type": "string", "minLength": 1, "maxLength": 80 }
            },
            "family_name_contains": { "type": ["string", "null"], "minLength": 1, "maxLength": 256 },
            "type_name_contains": { "type": ["string", "null"], "minLength": 1, "maxLength": 256 },
            "usage": { "type": ["string", "null"], "enum": ["all", "placed", "unused"] },
            "page_size": { "type": ["integer", "null"], "minimum": 1, "maximum": 200 },
            "cursor": { "type": ["string", "null"], "minLength": 1, "maxLength": 2048 }
          }
        }
        """);

    public static JsonElement Output { get; } = Parse(
        """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["context", "summary", "returned_count", "complete", "next_cursor", "types"],
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
            "summary": {
              "type": "object",
              "additionalProperties": false,
              "required": ["matched_type_count", "distinct_family_count", "placed_type_count", "placed_instance_count", "used_family_count", "unused_family_count"],
              "properties": {
                "matched_type_count": { "type": "integer", "minimum": 0 },
                "distinct_family_count": { "type": "integer", "minimum": 0 },
                "placed_type_count": { "type": "integer", "minimum": 0 },
                "placed_instance_count": { "type": "integer", "minimum": 0 },
                "used_family_count": { "type": "integer", "minimum": 0 },
                "unused_family_count": { "type": "integer", "minimum": 0 }
              }
            },
            "returned_count": { "type": "integer", "minimum": 0 },
            "complete": { "type": "boolean" },
            "next_cursor": { "type": ["string", "null"] },
            "types": {
              "type": "array",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "required": ["type_ref", "family_ref", "category_token", "category_name", "family_name", "type_name", "family_placement_type", "family_is_in_place", "placed_instance_count", "is_used"],
                "properties": {
                  "type_ref": { "type": "string" },
                  "family_ref": { "type": "string" },
                  "category_token": { "type": "string" },
                  "category_name": { "type": "string" },
                  "family_name": { "type": "string" },
                  "type_name": { "type": "string" },
                  "family_placement_type": {
                    "type": "string",
                    "enum": ["one_level_based", "one_level_based_hosted", "two_levels_based", "view_based", "work_plane_based", "curve_based", "curve_based_detail", "curve_driven_structural", "adaptive", "invalid", "unknown"]
                  },
                  "family_is_in_place": { "type": "boolean" },
                  "placed_instance_count": { "type": "integer", "minimum": 0 },
                  "is_used": { "type": "boolean" }
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
