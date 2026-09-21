using System.Text.Json;

namespace RevitMCP.Server;

internal static class Cap0004JsonSchemas
{
    public static JsonElement Input { get; } = Parse(
        """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["document_id", "element_refs"],
          "properties": {
            "instance_id": {
              "type": ["string", "null"]
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
            "source": {
              "type": "string",
              "enum": ["instance", "type", "both"],
              "default": "both"
            },
            "name_contains": {
              "type": "string",
              "minLength": 1,
              "maxLength": 256
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
          "required": ["context", "elements", "matched_count", "truncated", "parameters"],
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
                "type": "object",
                "additionalProperties": false,
                "required": ["element_ref", "status"],
                "properties": {
                  "element_ref": { "type": "string" },
                  "status": {
                    "type": "string",
                    "enum": ["ok", "not_found"]
                  }
                }
              }
            },
            "matched_count": {
              "type": "integer",
              "minimum": 0
            },
            "truncated": {
              "type": "boolean"
            },
            "parameters": {
              "type": "array",
              "maxItems": 100,
              "items": {
                "type": "object",
                "additionalProperties": false,
                "required": [
                  "parameter_ref",
                  "name",
                  "source",
                  "identity",
                  "data_type",
                  "present_on_count",
                  "read_only_on_count"
                ],
                "properties": {
                  "parameter_ref": { "type": "string" },
                  "name": { "type": "string" },
                  "source": {
                    "type": "string",
                    "enum": ["instance", "type"]
                  },
                  "identity": {
                    "oneOf": [
                      {
                        "type": "object",
                        "additionalProperties": false,
                        "required": ["kind", "parameter_type_id"],
                        "properties": {
                          "kind": { "const": "built_in" },
                          "parameter_type_id": { "type": "string" }
                        }
                      },
                      {
                        "type": "object",
                        "additionalProperties": false,
                        "required": ["kind", "guid"],
                        "properties": {
                          "kind": { "const": "shared" },
                          "guid": { "type": "string" }
                        }
                      },
                      {
                        "type": "object",
                        "additionalProperties": false,
                        "required": ["kind"],
                        "properties": {
                          "kind": { "const": "local" }
                        }
                      }
                    ]
                  },
                  "data_type": {
                    "oneOf": [
                      {
                        "type": "object",
                        "additionalProperties": false,
                        "required": ["kind", "forge_type_id"],
                        "properties": {
                          "kind": { "const": "measurable_spec" },
                          "forge_type_id": { "type": "string" }
                        }
                      },
                      {
                        "type": "object",
                        "additionalProperties": false,
                        "required": ["kind", "forge_type_id"],
                        "properties": {
                          "kind": { "const": "spec" },
                          "forge_type_id": { "type": "string" }
                        }
                      },
                      {
                        "type": "object",
                        "additionalProperties": false,
                        "required": ["kind", "forge_type_id"],
                        "properties": {
                          "kind": { "const": "category" },
                          "forge_type_id": { "type": "string" }
                        }
                      },
                      {
                        "type": "object",
                        "additionalProperties": false,
                        "required": ["kind"],
                        "properties": {
                          "kind": { "const": "unknown" },
                          "forge_type_id": { "type": "string" }
                        }
                      }
                    ]
                  },
                  "present_on_count": {
                    "type": "integer",
                    "minimum": 1,
                    "maximum": 10
                  },
                  "read_only_on_count": {
                    "type": "integer",
                    "minimum": 0,
                    "maximum": 10
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
