using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using RevitMCP.Contracts;

namespace RevitMCP.Server;

internal static class McpJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = ContractJson.CreateOptions();
        options.WriteIndented = false;
        options.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
        options.TypeInfoResolver = new DefaultJsonTypeInfoResolver();
        return options;
    }
}
