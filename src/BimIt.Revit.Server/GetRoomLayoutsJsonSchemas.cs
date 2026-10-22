using System.Text.Json;

namespace BimIt.Revit.Server;

internal static class GetRoomLayoutsJsonSchemas
{
    public static JsonElement Input { get; } = Parse(
        """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["document_id", "element_refs"],
          "properties": {
            "instance_id": { "type": ["string", "null"] },
            "document_id": { "type": "string" },
            "element_refs": {
              "type": "array",
              "minItems": 1,
              "maxItems": 20,
              "items": { "type": "string", "minLength": 1, "maxLength": 128 }
            }
          }
        }
        """);

    public static JsonElement Output { get; } = Parse(
        """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["context", "length_unit", "chord_tolerance_m", "opening_scan_truncated", "items"],
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
            "length_unit": { "type": "string", "const": "m" },
            "chord_tolerance_m": { "type": "number" },
            "opening_scan_truncated": { "type": "boolean" },
            "items": {
              "type": "array",
              "maxItems": 20,
              "items": {
                "type": "object",
                "additionalProperties": false,
                "required": ["element_ref", "status"],
                "properties": {
                  "element_ref": { "type": "string" },
                  "status": { "type": "string", "enum": ["ok", "not_found", "not_a_room", "boundary_unavailable"] },
                  "name": { "type": ["string", "null"] },
                  "number": { "type": ["string", "null"] },
                  "level_name": { "type": ["string", "null"] },
                  "centroid_x_m": { "type": ["number", "null"] },
                  "centroid_y_m": { "type": ["number", "null"] },
                  "loops_truncated": { "type": ["boolean", "null"] },
                  "features_truncated": { "type": ["boolean", "null"] },
                  "loops": {
                    "type": ["array", "null"],
                    "maxItems": 4,
                    "items": {
                      "type": "object",
                      "additionalProperties": false,
                      "required": ["vertices_truncated", "vertices"],
                      "properties": {
                        "vertices_truncated": { "type": "boolean" },
                        "vertices": {
                          "type": "array",
                          "maxItems": 48,
                          "items": {
                            "type": "object",
                            "additionalProperties": false,
                            "required": ["x_m", "y_m"],
                            "properties": {
                              "x_m": { "type": "number" },
                              "y_m": { "type": "number" }
                            }
                          }
                        }
                      }
                    }
                  },
                  "features": {
                    "type": ["array", "null"],
                    "maxItems": 12,
                    "items": {
                      "type": "object",
                      "additionalProperties": false,
                      "required": ["kind", "x_m", "y_m"],
                      "properties": {
                        "kind": { "type": "string", "enum": ["door", "window"] },
                        "x_m": { "type": "number" },
                        "y_m": { "type": "number" },
                        "facing_x": { "type": ["number", "null"] },
                        "facing_y": { "type": ["number", "null"] },
                        "width_m": { "type": ["number", "null"] }
                      }
                    }
                  }
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
