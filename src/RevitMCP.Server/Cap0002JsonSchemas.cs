using System.Text.Json;

namespace RevitMCP.Server;

internal static class Cap0002JsonSchemas
{
    public static JsonElement Input { get; } = Parse(
        """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["scope", "filters"],
          "properties": {
            "instance_id": {
              "type": "string"
            },
            "document_id": {
              "type": "string"
            },
            "scope": {
              "type": "string",
              "enum": ["document", "active_view"]
            },
            "filters": {
              "type": "object",
              "additionalProperties": false,
              "minProperties": 1,
              "properties": {
                "category_names": {
                  "type": "array",
                  "minItems": 1,
                  "maxItems": 20,
                  "items": {
                    "type": "string",
                    "minLength": 1,
                    "maxLength": 256
                  }
                },
                "family_names": {
                  "type": "array",
                  "minItems": 1,
                  "maxItems": 20,
                  "items": {
                    "type": "string",
                    "minLength": 1,
                    "maxLength": 256
                  }
                },
                "type_names": {
                  "type": "array",
                  "minItems": 1,
                  "maxItems": 20,
                  "items": {
                    "type": "string",
                    "minLength": 1,
                    "maxLength": 256
                  }
                },
                "level_names": {
                  "type": "array",
                  "minItems": 1,
                  "maxItems": 20,
                  "items": {
                    "type": "string",
                    "minLength": 1,
                    "maxLength": 256
                  }
                },
                "text_contains": {
                  "type": "string",
                  "minLength": 1,
                  "maxLength": 256
                }
              }
            },
            "limit": {
              "type": "integer",
              "minimum": 1,
              "maximum": 100,
              "default": 50
            }
          }
        }
        """);

    public static JsonElement Output { get; } = Parse(
        """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["context", "matched_count", "truncated", "element_refs"],
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
            "matched_count": {
              "type": "integer",
              "minimum": 0
            },
            "truncated": {
              "type": "boolean"
            },
            "element_refs": {
              "type": "array",
              "maxItems": 100,
              "uniqueItems": true,
              "items": {
                "type": "string"
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
