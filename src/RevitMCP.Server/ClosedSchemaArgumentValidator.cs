using System.Text.Json;

namespace RevitMCP.Server;

internal static class ClosedSchemaArgumentValidator
{
    public static bool HasUnexpectedProperties(
        IDictionary<string, JsonElement>? arguments,
        JsonElement schema)
    {
        if (arguments is null)
        {
            return false;
        }

        if (IsClosedObject(schema) && HasUnexpectedNames(arguments.Keys, schema))
        {
            return true;
        }

        return HasUnexpectedNestedProperties(arguments, schema);
    }

    internal static bool HasUnexpectedProperties(JsonElement value, JsonElement schema)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (IsClosedObject(schema) && HasUnexpectedNames(EnumerateNames(value), schema))
        {
            return true;
        }

        if (!TryGetDeclaredProperties(schema, out var declared))
        {
            return false;
        }

        foreach (var property in declared.EnumerateObject())
        {
            if (value.TryGetProperty(property.Name, out var child)
                && child.ValueKind == JsonValueKind.Object
                && HasUnexpectedProperties(child, property.Value))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasUnexpectedNestedProperties(
        IDictionary<string, JsonElement> arguments,
        JsonElement schema)
    {
        if (!TryGetDeclaredProperties(schema, out var declared))
        {
            return false;
        }

        foreach (var property in declared.EnumerateObject())
        {
            if (arguments.TryGetValue(property.Name, out var child)
                && child.ValueKind == JsonValueKind.Object
                && HasUnexpectedProperties(child, property.Value))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasUnexpectedNames(IEnumerable<string> names, JsonElement schema)
    {
        var allowed = GetDeclaredNames(schema);
        foreach (var name in names)
        {
            if (!allowed.Contains(name))
            {
                return true;
            }
        }

        return false;
    }

    private static HashSet<string> GetDeclaredNames(JsonElement schema)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        if (!TryGetDeclaredProperties(schema, out var properties))
        {
            return names;
        }

        foreach (var property in properties.EnumerateObject())
        {
            names.Add(property.Name);
        }

        return names;
    }

    private static IEnumerable<string> EnumerateNames(JsonElement value)
    {
        foreach (var property in value.EnumerateObject())
        {
            yield return property.Name;
        }
    }

    private static bool TryGetDeclaredProperties(JsonElement schema, out JsonElement properties)
    {
        if (schema.ValueKind == JsonValueKind.Object
            && schema.TryGetProperty("properties", out properties)
            && properties.ValueKind == JsonValueKind.Object)
        {
            return true;
        }

        properties = default;
        return false;
    }

    private static bool IsClosedObject(JsonElement schema)
    {
        return schema.ValueKind == JsonValueKind.Object
            && schema.TryGetProperty("additionalProperties", out var additional)
            && additional.ValueKind == JsonValueKind.False;
    }
}
