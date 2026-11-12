using System.Text.Json;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Server;

internal static class ClosedSchemaArgumentValidator
{
    public static bool IsInvalid(
        IDictionary<string, JsonElement>? arguments,
        JsonElement schema)
    {
        return !JsonSchemaSubset.MatchesArguments(arguments, schema);
    }

    public static bool HasUnexpectedProperties(
        IDictionary<string, JsonElement>? arguments,
        JsonElement schema)
    {
        return JsonSchemaSubset.HasUnexpectedProperties(arguments, schema);
    }

    internal static bool HasUnexpectedProperties(JsonElement value, JsonElement schema)
    {
        return JsonSchemaSubset.HasUnexpectedProperties(value, schema);
    }
}
