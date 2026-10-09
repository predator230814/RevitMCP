using System.Text.Json;

namespace RevitMCP.Server;

internal static class GetWarningsJsonSchemas
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
            "severity": {
              "type": "string",
              "enum": ["warning", "error", "document_corruption", "other"]
            },
            "failure_key": { "type": "string" },
            "element_refs": {
              "type": "array",
              "minItems": 1,
              "maxItems": 10,
              "uniqueItems": true,
              "items": { "type": "string" }
            },
            "max_warnings": {
              "type": "integer",
              "default": 25,
              "minimum": 1,
              "maximum": 100
            },
            "max_elements_per_warning": {
              "type": "integer",
              "default": 10,
              "minimum": 1,
              "maximum": 20
            }
          }
        }
        """);

    public static JsonElement Output { get; } = Parse(
        """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["context", "matched_count", "counts_by_severity", "definitions", "warnings", "truncated", "truncation_reasons"],
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
            "matched_count": { "type": "integer", "minimum": 0 },
            "counts_by_severity": {
              "type": "object",
              "additionalProperties": false,
              "required": ["warning", "error", "document_corruption", "other"],
              "properties": {
                "warning": { "type": "integer", "minimum": 0 },
                "error": { "type": "integer", "minimum": 0 },
                "document_corruption": { "type": "integer", "minimum": 0 },
                "other": { "type": "integer", "minimum": 0 }
              }
            },
            "definitions": {
              "type": "array",
              "maxItems": 50,
              "items": {
                "type": "object",
                "additionalProperties": false,
                "required": ["failure_key", "severity", "matched_count"],
                "properties": {
                  "failure_key": { "type": "string" },
                  "severity": { "type": "string", "enum": ["warning", "error", "document_corruption", "other"] },
                  "matched_count": { "type": "integer", "minimum": 1 }
                }
              }
            },
            "warnings": {
              "type": "array",
              "maxItems": 100,
              "items": {
                "type": "object",
                "additionalProperties": false,
                "required": ["failure_key", "severity", "description_text", "description_truncated", "has_resolutions", "elements", "elements_truncated", "unresolved_element_count"],
                "properties": {
                  "failure_key": { "type": "string" },
                  "severity": { "type": "string", "enum": ["warning", "error", "document_corruption", "other"] },
                  "description_text": { "type": "string", "maxLength": 512 },
                  "description_truncated": { "type": "boolean" },
                  "has_resolutions": { "type": "boolean" },
                  "elements": {
                    "type": "array",
                    "maxItems": 20,
                    "items": {
                      "type": "object",
                      "additionalProperties": false,
                      "required": ["element_ref", "role"],
                      "properties": {
                        "element_ref": { "type": "string" },
                        "role": { "type": "string", "enum": ["failing", "additional"] }
                      }
                    }
                  },
                  "elements_truncated": { "type": "boolean" },
                  "unresolved_element_count": { "type": "integer", "minimum": 0 }
                }
              }
            },
            "unmatched_element_refs": {
              "type": "array",
              "maxItems": 10,
              "items": { "type": "string" }
            },
            "truncated": { "type": "boolean" },
            "truncation_reasons": {
              "type": "array",
              "uniqueItems": true,
              "items": { "type": "string", "enum": ["definitions", "elements", "warnings"] }
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
