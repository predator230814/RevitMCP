using System.Text.Json;

namespace RevitMCP.Server;

internal static class Cap0001JsonSchemas
{
    public static JsonElement Input { get; } = Parse(
        """
        {
          "type": "object",
          "properties": {
            "instance_id": {
              "type": "string"
            }
          },
          "additionalProperties": false
        }
        """);

    public static JsonElement Output { get; } = Parse(
        """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["instance", "document", "active_view", "selection"],
          "properties": {
            "instance": {
              "type": "object",
              "additionalProperties": false,
              "required": ["instance_id", "revit_version", "revit_build"],
              "properties": {
                "instance_id": { "type": "string" },
                "revit_version": { "type": "string" },
                "revit_build": { "type": "string" }
              }
            },
            "document": {
              "anyOf": [
                { "type": "null" },
                {
                  "type": "object",
                  "additionalProperties": false,
                  "required": ["title", "kind", "is_workshared", "is_model_in_cloud", "is_read_only", "is_modified"],
                  "properties": {
                    "title": { "type": "string" },
                    "kind": { "type": "string", "enum": ["project", "family"] },
                    "is_workshared": { "type": "boolean" },
                    "is_model_in_cloud": { "type": "boolean" },
                    "is_read_only": { "type": "boolean" },
                    "is_modified": { "type": "boolean" }
                  }
                }
              ]
            },
            "active_view": {
              "anyOf": [
                { "type": "null" },
                {
                  "type": "object",
                  "additionalProperties": false,
                  "required": ["element_id", "name", "view_type"],
                  "properties": {
                    "element_id": { "type": "string" },
                    "name": { "type": "string" },
                    "view_type": { "type": "string" }
                  }
                }
              ]
            },
            "selection": {
              "type": "object",
              "additionalProperties": false,
              "required": ["count"],
              "properties": {
                "count": { "type": "integer", "minimum": 0 }
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
