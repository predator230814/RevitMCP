using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace BimIt.Revit.Contracts;

/// <summary>
/// Provider-neutral bound on a serialized structured read result. It matches the BIM-IT Assistant
/// read-facade budget so a capability can enforce the limit without referencing the Assistant.
/// </summary>
public static class ReadResultBudget
{
    public const int MaxCharacters = 120_000;

    public static JsonSerializerOptions Options { get; } = CreateOptions();

    public static JsonSerializerOptions CreateOptions()
    {
        var options = ContractJson.CreateOptions();
        options.WriteIndented = false;
        options.TypeInfoResolver = new DefaultJsonTypeInfoResolver();
        return options;
    }

    public static string Serialize<T>(T value)
    {
        return JsonSerializer.Serialize(value, Options);
    }

    public static int Measure<T>(T value)
    {
        return Serialize(value).Length;
    }
}
