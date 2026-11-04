using System.Text.Json;
using System.Text.Json.Serialization;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Server;

internal static class McpJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = ReadResultBudget.CreateOptions();
        options.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
        return options;
    }
}
