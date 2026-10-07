using System.Text.Json;

namespace RevitMCP.Server;

internal static class RequestParameterUpdateReviewJsonSchemas
{
    public static JsonElement Input { get; } = Parse(
        """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["instance_id", "intent_ref"],
          "properties": {
            "instance_id": {
              "type": "string",
              "minLength": 1,
              "maxLength": 128
            },
            "intent_ref": {
              "type": "string",
              "minLength": 1,
              "maxLength": 128
            }
          }
        }
        """);

    public static JsonElement Output { get; } = Parse(
        """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["status"],
          "properties": {
            "status": {
              "type": "string",
              "enum": ["started", "already_active", "busy", "unavailable", "terminal"]
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
