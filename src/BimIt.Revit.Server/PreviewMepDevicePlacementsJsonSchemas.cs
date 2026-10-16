using System.Text.Json;

namespace BimIt.Revit.Server;

internal static class PreviewMepDevicePlacementsJsonSchemas
{
    public static JsonElement Input { get; } = Parse(
        """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["document_id", "template_element_ref", "length_unit_id", "placements"],
          "properties": {
            "instance_id": { "type": "string", "minLength": 1, "maxLength": 128 },
            "document_id": { "type": "string", "minLength": 1, "maxLength": 128 },
            "template_element_ref": { "type": "string", "minLength": 1, "maxLength": 128 },
            "length_unit_id": { "type": "string", "minLength": 1, "maxLength": 256 },
            "placements": {
              "type": "array",
              "minItems": 1,
              "maxItems": 20,
              "items": {
                "type": "object",
                "additionalProperties": false,
                "required": ["offset_along_host", "vertical_offset"],
                "properties": {
                  "offset_along_host": { "type": "number" },
                  "vertical_offset": { "type": "number" }
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
          "required": ["context", "ready", "template", "items"],
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
            "ready": { "type": "boolean" },
            "template": {
              "type": "object",
              "additionalProperties": false,
              "required": ["element_ref", "status"],
              "properties": {
                "element_ref": { "type": "string" },
                "status": {
                  "type": "string",
                  "enum": ["ok", "not_found", "not_family_instance", "unsupported_category", "unsupported_placement", "unsupported_host", "unsupported_host_geometry", "unsupported_location", "unsupported_level"]
                },
                "category_name": { "type": "string" },
                "family_name": { "type": "string" },
                "type_name": { "type": "string" },
                "level_name": { "type": "string" },
                "host_element_ref": { "type": "string" },
                "host_name": { "type": "string" }
              }
            },
            "items": {
              "type": "array",
              "maxItems": 20,
              "items": {
                "type": "object",
                "additionalProperties": false,
                "required": ["offset_along_host", "vertical_offset", "status"],
                "properties": {
                  "offset_along_host": { "type": "number" },
                  "vertical_offset": { "type": "number" },
                  "status": {
                    "type": "string",
                    "enum": ["ok", "out_of_host_bounds", "occupied", "unsupported_position"]
                  }
                }
              }
            },
            "intent_ref": { "type": "string" },
            "intent_fingerprint": { "type": "string" },
            "expires_at": { "type": "string" }
          }
        }
        """);

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
