using System.Text.Json;

namespace RevitMCP.Server;

internal static class Cap0007JsonSchemas
{
    private const string ValueSchema =
        """
        {
          "oneOf": [
            {
              "type": "object",
              "additionalProperties": false,
              "required": ["kind", "value"],
              "properties": {
                "kind": { "const": "string" },
                "value": { "type": "string", "maxLength": 512 }
              }
            },
            {
              "type": "object",
              "additionalProperties": false,
              "required": ["kind", "value"],
              "properties": {
                "kind": { "const": "integer" },
                "value": {
                  "type": "integer",
                  "minimum": -2147483648,
                  "maximum": 2147483647
                }
              }
            },
            {
              "type": "object",
              "additionalProperties": false,
              "required": ["kind", "value", "unit_type_id"],
              "properties": {
                "kind": { "const": "quantity" },
                "value": { "type": "number" },
                "unit_type_id": { "type": "string", "minLength": 1 }
              }
            }
          ]
        }
        """;

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

    private const string BeforeSchema =
        """
        {
          "oneOf": [
            {
              "type": "object",
              "additionalProperties": false,
              "required": ["has_value"],
              "properties": {
                "has_value": { "const": false }
              }
            },
            {
              "type": "object",
              "additionalProperties": false,
              "required": ["has_value", "value"],
              "properties": {
                "has_value": { "const": true },
                "value": {{ValueSchema}}
              }
            }
          ]
        }
        """;

    private const string EligibleItemSchema =
        """
        {
          "type": "object",
          "additionalProperties": false,
          "required": [
            "element_ref",
            "parameter_ref",
            "status",
            "element_name",
            "element_name_truncated",
            "category_name",
            "category_name_truncated",
            "parameter_name",
            "parameter_name_truncated",
            "data_type",
            "before",
            "proposed"
          ],
          "properties": {
            "element_ref": { "type": "string" },
            "parameter_ref": { "type": "string" },
            "status": {
              "type": "string",
              "enum": ["ok", "no_change"]
            },
            "element_name": { "type": "string", "maxLength": 512 },
            "element_name_truncated": { "type": "boolean" },
            "category_name": { "type": "string", "maxLength": 512 },
            "category_name_truncated": { "type": "boolean" },
            "parameter_name": { "type": "string", "maxLength": 512 },
            "parameter_name_truncated": { "type": "boolean" },
            "data_type": {{DataTypeSchema}},
            "before": {{BeforeSchema}},
            "proposed": {{ValueSchema}}
          }
        }
        """;

    private const string ItemsSchema =
        """
        {
          "type": "array",
          "minItems": 1,
          "maxItems": 20,
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
                      "parameter_ref_not_found",
                      "element_not_found",
                      "unsupported_parameter_source",
                      "parameter_not_present",
                      "parameter_not_writable",
                      "value_type_mismatch",
                      "invalid_unit",
                      "unsupported_value"
                    ]
                  }
                }
              },
              {{EligibleItemSchema}}
            ]
          }
        }
        """;

    private const string ReadyItemsSchema =
        """
        {
          "type": "array",
          "minItems": 1,
          "maxItems": 20,
          "items": {{ReadyItemSchema}}
        }
        """;

    private const string ReadyItemSchema =
        """
        {
          "type": "object",
          "additionalProperties": false,
          "required": [
            "element_ref",
            "parameter_ref",
            "status",
            "element_name",
            "element_name_truncated",
            "category_name",
            "category_name_truncated",
            "parameter_name",
            "parameter_name_truncated",
            "data_type",
            "before",
            "proposed"
          ],
          "properties": {
            "element_ref": { "type": "string" },
            "parameter_ref": { "type": "string" },
            "status": { "const": "ok" },
            "element_name": { "type": "string", "maxLength": 512 },
            "element_name_truncated": { "type": "boolean" },
            "category_name": { "type": "string", "maxLength": 512 },
            "category_name_truncated": { "type": "boolean" },
            "parameter_name": { "type": "string", "maxLength": 512 },
            "parameter_name_truncated": { "type": "boolean" },
            "data_type": {{DataTypeSchema}},
            "before": {{BeforeSchema}},
            "proposed": {{ValueSchema}}
          }
        }
        """;

    public static JsonElement Input { get; } = Parse(
        """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["document_id", "updates"],
          "properties": {
            "instance_id": {
              "type": ["string", "null"]
            },
            "document_id": {
              "type": "string"
            },
            "updates": {
              "type": "array",
              "minItems": 1,
              "maxItems": 20,
              "items": {
                "type": "object",
                "additionalProperties": false,
                "required": ["element_ref", "parameter_ref", "value"],
                "properties": {
                  "element_ref": { "type": "string" },
                  "parameter_ref": { "type": "string" },
                  "value": {{ValueSchema}}
                }
              }
            }
          }
        }
        """);

    public static JsonElement Output { get; } = Parse(
        """
        {
          "oneOf": [
            {
              "type": "object",
              "additionalProperties": false,
              "required": ["context", "ready", "items"],
              "properties": {
                "context": {{ContextSchema}},
                "ready": { "const": false },
                "items": {{ItemsSchema}}
              }
            },
            {
              "type": "object",
              "additionalProperties": false,
              "required": ["context", "ready", "items", "intent_ref", "intent_fingerprint", "expires_at"],
              "properties": {
                "context": {{ContextSchema}},
                "ready": { "const": true },
                "items": {{ReadyItemsSchema}},
                "intent_ref": { "type": "string" },
                "intent_fingerprint": { "type": "string" },
                "expires_at": { "type": "string", "format": "date-time" }
              }
            }
          ]
        }
        """);

    private const string ContextSchema =
        """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["instance_id", "document_id"],
          "properties": {
            "instance_id": { "type": "string" },
            "document_id": { "type": "string" }
          }
        }
        """;

    private static JsonElement Parse(string json)
    {
        var resolved = json
            .Replace("{{ValueSchema}}", ValueSchema, StringComparison.Ordinal)
            .Replace("{{DataTypeSchema}}", DataTypeSchema, StringComparison.Ordinal)
            .Replace("{{BeforeSchema}}", BeforeSchema.Replace("{{ValueSchema}}", ValueSchema, StringComparison.Ordinal), StringComparison.Ordinal)
            .Replace("{{ReadyItemsSchema}}", ReadyItemsSchema
                .Replace("{{ReadyItemSchema}}", ReadyItemSchema
                    .Replace("{{DataTypeSchema}}", DataTypeSchema, StringComparison.Ordinal)
                    .Replace("{{BeforeSchema}}", BeforeSchema.Replace("{{ValueSchema}}", ValueSchema, StringComparison.Ordinal), StringComparison.Ordinal)
                    .Replace("{{ValueSchema}}", ValueSchema, StringComparison.Ordinal), StringComparison.Ordinal), StringComparison.Ordinal)
            .Replace("{{ItemsSchema}}", ItemsSchema
                .Replace("{{EligibleItemSchema}}", EligibleItemSchema
                    .Replace("{{DataTypeSchema}}", DataTypeSchema, StringComparison.Ordinal)
                    .Replace("{{BeforeSchema}}", BeforeSchema.Replace("{{ValueSchema}}", ValueSchema, StringComparison.Ordinal), StringComparison.Ordinal)
                    .Replace("{{ValueSchema}}", ValueSchema, StringComparison.Ordinal), StringComparison.Ordinal), StringComparison.Ordinal)
            .Replace("{{ContextSchema}}", ContextSchema, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(resolved);
        return document.RootElement.Clone();
    }
}
