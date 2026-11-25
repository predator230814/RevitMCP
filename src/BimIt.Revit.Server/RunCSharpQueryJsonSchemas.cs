using System.Text.Json;

namespace BimIt.Revit.Server;

internal static class RunCSharpQueryJsonSchemas
{
    public static JsonElement Input { get; } = Parse(
        """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["document_id", "source"],
          "properties": {
            "instance_id": { "type": ["string", "null"], "minLength": 1 },
            "document_id": { "type": "string", "minLength": 1 },
            "source": { "type": "string", "minLength": 1, "maxLength": 8000 }
          }
        }
        """);

    public static JsonElement Output { get; } = Parse(
        """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["result", "source_sha256", "cache_key", "policy_version", "revit_api", "compile_outcome", "duration_ms", "result_characters"],
          "properties": {
            "result": {},
            "source_sha256": { "type": "string", "pattern": "^[0-9a-f]{64}$" },
            "cache_key": { "type": "string", "pattern": "^[0-9a-f]{64}$" },
            "policy_version": { "type": "integer", "const": 1 },
            "revit_api": {
              "type": "object",
              "additionalProperties": false,
              "required": ["name", "version"],
              "properties": {
                "name": { "type": "string", "const": "RevitAPI" },
                "version": { "type": "string", "const": "26.5.0.0" }
              }
            },
            "compile_outcome": { "type": "string", "const": "success" },
            "duration_ms": { "type": "integer", "minimum": 0 },
            "result_characters": { "type": "integer", "minimum": 0, "maximum": 16000 }
          }
        }
        """);

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
