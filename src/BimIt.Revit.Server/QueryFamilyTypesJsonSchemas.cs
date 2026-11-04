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
            "usage": {
              "type": ["string", "null"],
              "enum": ["all", "placed", "unused", null],
              "description": "Which types to match. all (default) matches every type; placed matches types with at least one instance; unused matches types with none. The summary describes the matched set, so use usage=all for an unfiltered used/unused family classification."
            },
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
              "description": "Exact totals over the entire matched set, repeated unchanged on every page. They are not totals of the current page. Family counts are relative to the matched set.",
              "required": ["matched_type_count", "distinct_family_count", "placed_type_count", "placed_instance_count", "used_family_count", "unused_family_count"],
              "properties": {
                "matched_type_count": { "type": "integer", "minimum": 0, "description": "Number of matched family types." },
                "distinct_family_count": { "type": "integer", "minimum": 0, "description": "Number of distinct families among the matched types." },
                "placed_type_count": { "type": "integer", "minimum": 0, "description": "Matched types with at least one placed instance." },
                "placed_instance_count": { "type": "integer", "minimum": 0, "description": "Sum of placed instances over all matched types. Counts instances in the active host project document only; linked-document instances are excluded. Includes instances in groups and design options and nested shared family instances when Revit exposes them as project-document elements." },
                "used_family_count": { "type": "integer", "minimum": 0, "description": "Matched families with at least one matched type that has a placed instance." },
                "unused_family_count": { "type": "integer", "minimum": 0, "description": "Matched families whose matched types are all unplaced. Relative to the matched set: with usage=unused or a name filter it can count a family that has placed types outside the matched set. For an unfiltered classification use usage=all with no name filters, optionally narrowed by category_tokens." }
              }
            },
            "returned_count": { "type": "integer", "minimum": 0, "description": "Rows in this page. It can be smaller than page_size." },
            "complete": { "type": "boolean", "description": "True only on the last page. This is the authoritative end of a traversal; keep following next_cursor until it is true." },
            "next_cursor": { "type": ["string", "null"], "description": "Opaque cursor for the next page. Pass it back unchanged with the same query. Null when complete is true." },
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
                  "placed_instance_count": { "type": "integer", "minimum": 0, "description": "Instances of this type in the active host project document; linked-document instances are excluded. Includes instances in groups and design options and nested shared family instances when Revit exposes them as project-document elements." },
                  "is_used": { "type": "boolean", "description": "True when placed_instance_count is greater than zero." }
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
