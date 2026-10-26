using System.Text.Json;

namespace BimIt.Revit.Server;

internal static class PreviewRoomPatternPlacementsJsonSchemas
{
    public static JsonElement Input { get; } = Parse(
        """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["document_id", "placements"],
          "properties": {
            "instance_id": { "type": ["string", "null"] },
            "document_id": { "type": "string", "minLength": 1, "maxLength": 128 },
            "placements": {
              "type": "array",
              "minItems": 1,
              "maxItems": 80,
              "items": {
                "type": "object",
                "additionalProperties": false,
                "required": ["room_ref", "family_type_ref", "role", "placement_kind", "x_m", "y_m", "mounting_height_m", "quarter_turns"],
                "properties": {
                  "room_ref": { "type": "string", "minLength": 1, "maxLength": 128 },
                  "family_type_ref": { "type": "string", "minLength": 1, "maxLength": 128 },
                  "role": { "type": "string", "enum": ["receptacle", "switch", "luminaire", "smoke_detector", "thermostat", "data", "other"] },
                  "placement_kind": { "type": "string", "enum": ["boundary", "interior"] },
                  "x_m": { "type": "number" },
                  "y_m": { "type": "number" },
                  "mounting_height_m": { "type": "number", "minimum": 0, "maximum": 6 },
                  "quarter_turns": { "type": "integer", "minimum": 0, "maximum": 3 }
                }
              }
            }
          }
        }
        """);

    public static JsonElement Output { get; } = Parse(
        """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["context", "length_unit", "ready", "room_count", "items"],
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
            "ready": { "type": "boolean" },
            "room_count": { "type": "integer" },
            "items": {
              "type": "array",
              "maxItems": 80,
              "items": {
                "type": "object",
                "additionalProperties": false,
                "required": ["position", "status"],
                "properties": {
                  "position": { "type": "integer" },
                  "status": { "type": "string" }
                }
              }
            },
            "intent_ref": { "type": ["string", "null"] },
            "intent_fingerprint": { "type": ["string", "null"] },
            "expires_at": { "type": ["string", "null"] }
          }
        }
        """);

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
