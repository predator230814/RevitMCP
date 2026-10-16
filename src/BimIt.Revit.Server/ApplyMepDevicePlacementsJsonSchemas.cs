using System.Text.Json;

namespace BimIt.Revit.Server;

internal static class ApplyMepDevicePlacementsJsonSchemas
{
    public static JsonElement Input { get; } = Parse(
        """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["instance_id", "intent_ref"],
          "properties": {
            "instance_id": { "type": "string", "minLength": 1, "maxLength": 128 },
            "intent_ref": { "type": "string", "minLength": 1, "maxLength": 128 }
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
              "enum": [
                "applied",
                "approval_required",
                "unavailable",
                "in_progress",
                "stale",
                "transaction_failed",
                "committed_unverified",
                "indeterminate",
                "audit_failed"
              ]
            },
            "created_count": { "type": "integer", "minimum": 1, "maximum": 20 },
            "created_element_refs": {
              "type": "array",
              "minItems": 1,
              "maxItems": 20,
              "items": { "type": "string", "minLength": 1 }
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
