using System.Text.Json;
using System.Text.Json.Serialization;

namespace RevitMCP.Contracts;

public static class ContractJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    public static JsonSerializerOptions CreateOptions()
    {
        return new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
        };
    }
}
