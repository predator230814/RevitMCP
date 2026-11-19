using System.Text.Json;

namespace BimIt.Revit.Server;

internal static class LookupReadOperationsJsonSchemas
{
    public static JsonElement Input { get; } = Parse(
        """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "instance_id": { "type": ["string", "null"], "minLength": 1 },
            "operation": { "type": ["string", "null"], "minLength": 1, "maxLength": 64 },
            "contract_version": { "type": ["integer", "null"], "minimum": 1 },
            "query": { "type": ["string", "null"], "minLength": 1, "maxLength": 256 },
            "offset": { "type": ["integer", "null"], "minimum": 0 },
            "limit": { "type": ["integer", "null"], "minimum": 1, "maximum": 50 }
          }
        }
        """);

    public static JsonElement Output { get; } = Parse(
        """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["mode", "total", "returned_count", "next_offset", "operations"],
          "properties": {
            "mode": { "type": "string", "enum": ["search", "exact"] },
            "total": { "type": "integer", "minimum": 0 },
            "returned_count": { "type": "integer", "minimum": 0 },
            "next_offset": { "type": ["integer", "null"], "minimum": 0 },
            "operations": {
              "type": "array",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "required": ["id", "contract_version", "title", "description", "context", "effects", "pagination"],
                "properties": {
                  "id": { "type": "string" },
                  "contract_version": { "type": "integer", "minimum": 1 },
                  "title": { "type": "string" },
                  "description": { "type": "string" },
                  "context": { "type": "string" },
                  "effects": { "type": "string" },
                  "pagination": { "type": "string", "enum": ["none", "cursor"] },
                  "input_schema": { "type": "object" },
                  "output_schema": { "type": "object" }
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
