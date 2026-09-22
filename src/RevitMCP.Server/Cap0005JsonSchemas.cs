using System.Text.Json;

namespace RevitMCP.Server;

internal static class Cap0005JsonSchemas
{
    private const string DataTypeSchema =
        """
        {
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
        }
        """;

    public static JsonElement Input { get; } = Parse(
        """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["document_id", "reads"],
          "properties": {
            "instance_id": {
              "type": ["string", "null"]
            },
            "document_id": {
              "type": "string"
            },
            "reads": {
              "type": "array",
              "minItems": 1,
              "maxItems": 50,
              "uniqueItems": true,
              "items": {
                "type": "object",
                "additionalProperties": false,
                "required": ["element_ref", "parameter_ref"],
                "properties": {
                  "element_ref": { "type": "string" },
                  "parameter_ref": { "type": "string" }
                }
              }
            }
          }
        }
        """);

    public static JsonElement Output { get; } = Parse(
        $$"""
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["context", "items"],
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
            "items": {
              "type": "array",
              "minItems": 1,
              "maxItems": 50,
              "items": {
                "oneOf": [
                  {
                    "type": "object",
                    "additionalProperties": false,
                    "required": ["element_ref", "parameter_ref", "status"],
                    "properties": {
                      "element_ref": { "type": "string" },
                      "parameter_ref": { "type": "string" },
                      "status": {
                        "type": "string",
                        "enum": [
                          "element_not_found",
                          "parameter_ref_not_found",
                          "parameter_not_present",
                          "unsupported_value"
                        ]
                      }
                    }
                  },
                  {
                    "type": "object",
                    "additionalProperties": false,
                    "required": ["element_ref", "parameter_ref", "status", "data_type", "has_value"],
                    "properties": {
                      "element_ref": { "type": "string" },
                      "parameter_ref": { "type": "string" },
                      "status": { "const": "ok" },
                      "data_type": {{DataTypeSchema}},
                      "has_value": { "const": false }
                    }
                  },
                  {
                    "type": "object",
                    "additionalProperties": false,
                    "required": ["element_ref", "parameter_ref", "status", "data_type", "has_value", "value"],
                    "properties": {
                      "element_ref": { "type": "string" },
                      "parameter_ref": { "type": "string" },
                      "status": { "const": "ok" },
                      "data_type": {{DataTypeSchema}},
                      "has_value": { "const": true },
                      "value": {
                        "oneOf": [
                          {
                            "type": "object",
                            "additionalProperties": false,
                            "required": ["kind", "value", "truncated"],
                            "properties": {
                              "kind": { "const": "string" },
                              "value": { "type": "string", "maxLength": 512 },
                              "truncated": { "type": "boolean" }
                            }
                          },
                          {
                            "type": "object",
                            "additionalProperties": false,
                            "required": ["kind", "value"],
                            "properties": {
                              "kind": { "const": "integer" },
                              "value": { "type": "integer" }
                            }
                          },
                          {
                            "type": "object",
                            "additionalProperties": false,
                            "required": ["kind", "value", "unit_type_id"],
                            "properties": {
                              "kind": { "const": "quantity" },
                              "value": { "type": "number" },
                              "unit_type_id": { "type": "string" }
                            }
                          },
                          {
                            "type": "object",
                            "additionalProperties": false,
                            "required": ["kind", "resolved"],
                            "properties": {
                              "kind": { "const": "element_reference" },
                              "resolved": { "const": false }
                            }
                          },
                          {
                            "type": "object",
                            "additionalProperties": false,
                            "required": ["kind", "resolved"],
                            "properties": {
                              "kind": { "const": "element_reference" },
                              "resolved": { "const": true },
                              "name": { "type": "string", "maxLength": 512 },
                              "element_ref": { "type": "string" }
                            }
                          }
                        ]
                      }
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
