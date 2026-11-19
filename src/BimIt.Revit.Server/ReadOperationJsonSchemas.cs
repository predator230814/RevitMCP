using System.Text.Json;

namespace BimIt.Revit.Server;

internal static class ReadOperationJsonSchemas
{
    public static JsonElement Input { get; } = Parse(
        """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["document_id", "operation", "contract_version", "inputs"],
          "properties": {
            "instance_id": { "type": ["string", "null"], "minLength": 1 },
            "document_id": { "type": "string", "minLength": 1 },
            "operation": { "type": "string", "minLength": 1, "maxLength": 64 },
            "contract_version": { "type": "integer", "minimum": 1 },
            "inputs": { "type": "object" },
            "page_size": { "type": ["integer", "null"], "minimum": 1, "maximum": 200 },
            "cursor": { "type": ["string", "null"], "minLength": 1, "maxLength": 2048 }
          }
        }
        """);

    public static JsonElement Output { get; } = Parse(
        """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["context", "operation", "contract_version", "result", "complete", "next_cursor"],
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
            "operation": { "type": "string" },
            "contract_version": { "type": "integer", "minimum": 1 },
            "result": { "type": "object" },
            "complete": { "type": "boolean" },
            "next_cursor": { "type": ["string", "null"] }
          }
        }
        """);

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
