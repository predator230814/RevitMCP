using System.Text.Json;

namespace RevitMCP.Server;

internal static class Cap0006JsonSchemas
{
    public static JsonElement Input { get; } = Parse(
        """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["document_id", "seed_element_refs"],
          "properties": {
            "instance_id": {
              "type": ["string", "null"]
            },
            "document_id": {
              "type": "string"
            },
            "seed_element_refs": {
              "type": "array",
              "minItems": 1,
              "maxItems": 10,
              "uniqueItems": true,
              "items": {
                "type": "string"
              }
            },
            "domain": {
              "type": "string",
              "enum": ["hvac", "piping", "electrical", "cable_tray_conduit"]
            },
            "max_depth": {
              "type": "integer",
              "default": 3,
              "minimum": 1,
              "maximum": 10
            },
            "max_elements": {
              "type": "integer",
              "default": 100,
              "minimum": 10,
              "maximum": 250
            },
            "max_edges": {
              "type": "integer",
              "default": 200,
              "minimum": 10,
              "maximum": 500
            }
          }
        }
        """);

    public static JsonElement Output { get; } = Parse(
        """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["context", "seeds", "nodes", "edges", "truncated", "truncation_reasons"],
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
            "seeds": {
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
                    "enum": ["ok", "not_found", "no_connectors"]
                  }
                }
              }
            },
            "nodes": {
              "type": "array",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "required": ["element_ref", "depth"],
                "properties": {
                  "element_ref": { "type": "string" },
                  "depth": {
                    "type": "integer",
                    "minimum": 0
                  }
                }
              }
            },
            "edges": {
              "type": "array",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "required": ["element_ref_a", "element_ref_b", "domains"],
                "properties": {
                  "element_ref_a": { "type": "string" },
                  "element_ref_b": { "type": "string" },
                  "domains": {
                    "type": "array",
                    "uniqueItems": true,
                    "items": {
                      "type": "string",
                      "enum": ["hvac", "piping", "electrical", "cable_tray_conduit"]
                    }
                  }
                }
              }
            },
            "truncated": {
              "type": "boolean"
            },
            "truncation_reasons": {
              "type": "array",
              "uniqueItems": true,
              "items": {
                "type": "string",
                "enum": ["depth", "elements", "edges"]
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
