using System.Text.Json;

namespace BimIt.Revit.Server;

internal static class QueryParameterBindingsJsonSchemas
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
            "name_contains": { "type": ["string", "null"], "minLength": 1, "maxLength": 256 },
            "identity_kind": {
              "type": ["string", "null"],
              "enum": ["all", "shared", "local", "built_in", null]
            },
            "binding_kind": {
              "type": ["string", "null"],
              "enum": ["all", "instance", "type", null]
            },
            "category_tokens": {
              "type": ["array", "null"],
              "minItems": 1,
              "maxItems": 20,
              "uniqueItems": true,
              "items": { "type": "string", "minLength": 1, "maxLength": 80 }
            },
            "page_size": { "type": ["integer", "null"], "minimum": 1, "maximum": 100 },
            "cursor": { "type": ["string", "null"], "minLength": 1, "maxLength": 2048 }
          }
        }
        """);

    public static JsonElement Output { get; } = Parse(
        """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["context", "summary", "returned_count", "complete", "next_cursor", "bindings"],
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
            "summary": {
              "type": "object",
              "additionalProperties": false,
              "description": "Exact totals over the entire matched set, repeated unchanged on every page. They are not totals of the current page.",
              "required": ["matched_definition_count", "shared_count", "local_count", "built_in_count", "instance_bound_count", "type_bound_count", "distinct_category_count"],
              "properties": {
                "matched_definition_count": { "type": "integer", "minimum": 0, "description": "Number of matched parameter definitions." },
                "shared_count": { "type": "integer", "minimum": 0, "description": "Matched definitions that are shared parameters." },
                "local_count": { "type": "integer", "minimum": 0, "description": "Matched definitions that are project-local, not shared and not built in." },
                "built_in_count": { "type": "integer", "minimum": 0, "description": "Matched definitions that are built-in parameters." },
                "instance_bound_count": { "type": "integer", "minimum": 0, "description": "Matched definitions with an instance binding." },
                "type_bound_count": { "type": "integer", "minimum": 0, "description": "Matched definitions with a type binding." },
                "distinct_category_count": { "type": "integer", "minimum": 0, "description": "Distinct categories bound by the matched definitions." }
              }
            },
            "returned_count": { "type": "integer", "minimum": 0, "description": "Rows in this page. It can be smaller than page_size." },
            "complete": { "type": "boolean", "description": "True only on the last page. This is the authoritative end of a traversal; keep following next_cursor until it is true." },
            "next_cursor": { "type": ["string", "null"], "description": "Opaque cursor for the next page. Pass it back unchanged with the same query. Null when complete is true." },
            "bindings": {
              "type": "array",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "required": ["parameter_ref", "name", "identity", "data_type", "binding_kind", "categories"],
                "properties": {
                  "parameter_ref": { "type": "string", "description": "The same reference that revit_describe_parameters and revit_get_parameter_values use." },
                  "name": { "type": "string" },
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
                  "binding_kind": { "type": "string", "enum": ["instance", "type"] },
                  "categories": {
                    "type": "array",
                    "description": "The complete set of categories this definition is bound to. A category filter selects rows by any matching category but never shortens this set.",
                    "items": {
                      "type": "object",
                      "additionalProperties": false,
                      "required": ["category_token", "category_name"],
                      "properties": {
                        "category_token": { "type": "string" },
                        "category_name": { "type": "string" }
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
