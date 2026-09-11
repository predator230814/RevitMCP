using System.Text.Json;

namespace RevitMCP.Server;

internal static class ClosedSchemaArgumentValidator
{
    public static bool IsInvalid(
        IDictionary<string, JsonElement>? arguments,
        JsonElement schema)
    {
        return HasUnexpectedProperties(arguments, schema)
            || HasMissingOrUninterpretableRequired(arguments, schema);
    }

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

    private static bool HasMissingOrUninterpretableRequired(
        IDictionary<string, JsonElement>? arguments,
        JsonElement schema)
    {
        if (!TryGetRequiredNames(schema, out var required))
        {
            return false;
        }

        foreach (var name in required)
        {
            if (arguments is null || !arguments.TryGetValue(name, out var value))
            {
                return true;
            }

            if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                return true;
            }

            if (TryGetDeclaredPropertySchema(schema, name, out var propertySchema)
                && !MatchesDeclaredType(value, propertySchema))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesDeclaredType(JsonElement value, JsonElement schema)
    {
        if (!schema.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String)
        {
            return true;
        }

        return type.GetString() switch
        {
            "string" => value.ValueKind == JsonValueKind.String,
            "object" => value.ValueKind == JsonValueKind.Object,
            "array" => value.ValueKind == JsonValueKind.Array,
            "integer" or "number" => value.ValueKind == JsonValueKind.Number,
            "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            _ => true
        };
    }

    private static bool TryGetRequiredNames(JsonElement schema, out IReadOnlyList<string> required)
    {
        if (schema.ValueKind == JsonValueKind.Object
            && schema.TryGetProperty("required", out var requiredElement)
            && requiredElement.ValueKind == JsonValueKind.Array
            && requiredElement.GetArrayLength() > 0)
        {
            required = requiredElement.EnumerateArray()
                .Select(value => value.GetString())
                .OfType<string>()
                .ToArray();
            return required.Count > 0;
        }

        required = [];
        return false;
    }

    private static bool TryGetDeclaredPropertySchema(JsonElement schema, string name, out JsonElement propertySchema)
    {
        if (TryGetDeclaredProperties(schema, out var properties)
            && properties.TryGetProperty(name, out propertySchema)
            && propertySchema.ValueKind == JsonValueKind.Object)
        {
            return true;
        }

        propertySchema = default;
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
