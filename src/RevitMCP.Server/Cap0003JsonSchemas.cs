using System.Text.Json;

namespace RevitMCP.Server;

internal static class Cap0003JsonSchemas
{
    public static JsonElement Input { get; } = Parse(
        """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["document_id", "element_refs", "projection"],
          "properties": {
            "instance_id": {
              "type": "string"
            },
            "document_id": {
              "type": "string"
            },
            "element_refs": {
              "type": "array",
              "minItems": 1,
              "maxItems": 10,
              "uniqueItems": true,
              "items": {
                "type": "string"
              }
            },
            "projection": {
              "type": "object",
              "additionalProperties": false,
              "properties": {
                "fields": {
                  "type": "array",
                  "minItems": 1,
                  "maxItems": 5,
                  "uniqueItems": true,
                  "items": {
                    "type": "string",
                    "enum": ["name", "category_name", "family_name", "type_name", "level_name"]
                  }
                },
                "parameter_names": {
                  "type": "array",
                  "minItems": 1,
                  "maxItems": 10,
                  "uniqueItems": true,
                  "items": {
                    "type": "string",
                    "minLength": 1,
                    "maxLength": 256
                  }
                }
              },
              "anyOf": [
                { "required": ["fields"] },
                { "required": ["parameter_names"] }
              ]
            }
          }
        }
        """);

    public static JsonElement Output { get; } = Parse(
        """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["context", "elements"],
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
            "elements": {
              "type": "array",
              "minItems": 1,
              "maxItems": 10,
              "items": {
                "oneOf": [
                  {
                    "type": "object",
                    "additionalProperties": false,
                    "required": ["element_ref", "status"],
                    "properties": {
                      "element_ref": { "type": "string" },
                      "status": { "const": "not_found" }
                    }
                  },
                  {
                    "type": "object",
                    "additionalProperties": false,
                    "required": ["element_ref", "status"],
                    "dependentRequired": {
                      "parameters": ["parameters_truncated"],
                      "parameters_truncated": ["parameters"]
                    },
                    "properties": {
                      "element_ref": { "type": "string" },
                      "status": { "const": "ok" },
                      "name": { "type": ["string", "null"] },
                      "category_name": { "type": ["string", "null"] },
                      "family_name": { "type": ["string", "null"] },
                      "type_name": { "type": ["string", "null"] },
                      "level_name": { "type": ["string", "null"] },
                      "parameters": {
                        "type": "array",
                        "maxItems": 20,
                        "items": {
                          "type": "object",
                          "additionalProperties": false,
                          "required": ["name", "source", "value_text", "value_truncated"],
                          "properties": {
                            "name": { "type": "string" },
                            "source": {
                              "type": "string",
                              "enum": ["instance", "type"]
                            },
                            "value_text": {
                              "type": ["string", "null"],
                              "maxLength": 512
                            },
                            "value_truncated": { "type": "boolean" }
                          }
                        }
                      },
                      "parameters_truncated": { "type": "boolean" }
                    }
                  }
                ]
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
