using System.Text.Json;

namespace RevitMCP.Server;

internal static class ClosedSchemaArgumentValidator
{
    public static bool IsInvalid(
        IDictionary<string, JsonElement>? arguments,
        JsonElement schema)
    {
        return !MatchesObject(arguments, schema);
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

    private static bool MatchesObject(IDictionary<string, JsonElement>? arguments, JsonElement schema)
    {
        var properties = arguments ?? new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (IsClosedObject(schema) && HasUnexpectedNames(properties.Keys, schema))
        {
            return false;
        }

        if (HasMissingRequired(properties.Keys, schema))
        {
            return false;
        }

        if (!TryGetDeclaredProperties(schema, out var declared))
        {
            return true;
        }

        foreach (var property in declared.EnumerateObject())
        {
            if (!properties.TryGetValue(property.Name, out var value))
            {
                continue;
            }

            if (!MatchesValue(value, property.Value))
            {
                return false;
            }
        }

        return true;
    }

    private static bool MatchesObjectElement(JsonElement value, JsonElement schema)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (IsClosedObject(schema) && HasUnexpectedNames(EnumerateNames(value), schema))
        {
            return false;
        }

        if (HasMissingRequired(EnumerateNames(value), schema))
        {
            return false;
        }

        if (!TryGetDeclaredProperties(schema, out var declared))
        {
            return true;
        }

        foreach (var property in declared.EnumerateObject())
        {
            if (!value.TryGetProperty(property.Name, out var child))
            {
                continue;
            }

            if (!MatchesValue(child, property.Value))
            {
                return false;
            }
        }

        return true;
    }

    private static bool MatchesValue(JsonElement value, JsonElement schema)
    {
        if (!MatchesDeclaredType(value, schema))
        {
            return false;
        }

        if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return true;
        }

        if (!MatchesEnum(value, schema) || !MatchesConst(value, schema))
        {
            return false;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => MatchesStringConstraints(value, schema),
            JsonValueKind.Number => MatchesNumberConstraints(value, schema),
            JsonValueKind.Array => MatchesArray(value, schema),
            JsonValueKind.Object => MatchesObjectElement(value, schema),
            _ => true
        };
    }

    private static bool MatchesDeclaredType(JsonElement value, JsonElement schema)
    {
        if (!TryGetDeclaredTypes(schema, out var types))
        {
            return true;
        }

        foreach (var type in types)
        {
            if (MatchesSingleType(value, type))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesSingleType(JsonElement value, string type)
    {
        return type switch
        {
            "null" => value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined,
            "string" => value.ValueKind == JsonValueKind.String,
            "object" => value.ValueKind == JsonValueKind.Object,
            "array" => value.ValueKind == JsonValueKind.Array,
            "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
            "number" => value.ValueKind == JsonValueKind.Number,
            _ => true
        };
    }

    private static bool MatchesEnum(JsonElement value, JsonElement schema)
    {
        if (!schema.TryGetProperty("enum", out var enumeration) || enumeration.ValueKind != JsonValueKind.Array)
        {
            return true;
        }

        foreach (var candidate in enumeration.EnumerateArray())
        {
            if (JsonEquals(value, candidate))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesConst(JsonElement value, JsonElement schema)
    {
        return !schema.TryGetProperty("const", out var constant) || JsonEquals(value, constant);
    }

    private static bool MatchesStringConstraints(JsonElement value, JsonElement schema)
    {
        var text = value.GetString() ?? string.Empty;
        if (schema.TryGetProperty("minLength", out var minLength)
            && minLength.TryGetInt32(out var min)
            && text.Length < min)
        {
            return false;
        }

        if (schema.TryGetProperty("maxLength", out var maxLength)
            && maxLength.TryGetInt32(out var max)
            && text.Length > max)
        {
            return false;
        }

        return true;
    }

    private static bool MatchesNumberConstraints(JsonElement value, JsonElement schema)
    {
        if (!value.TryGetDecimal(out var number))
        {
            return false;
        }

        if (schema.TryGetProperty("minimum", out var minimum)
            && minimum.TryGetDecimal(out var min)
            && number < min)
        {
            return false;
        }

        if (schema.TryGetProperty("maximum", out var maximum)
            && maximum.TryGetDecimal(out var max)
            && number > max)
        {
            return false;
        }

        return true;
    }

    private static bool MatchesArray(JsonElement value, JsonElement schema)
    {
        var length = value.GetArrayLength();
        if (schema.TryGetProperty("minItems", out var minItems)
            && minItems.TryGetInt32(out var min)
            && length < min)
        {
            return false;
        }

        if (schema.TryGetProperty("maxItems", out var maxItems)
            && maxItems.TryGetInt32(out var max)
            && length > max)
        {
            return false;
        }

        if (schema.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Object)
        {
            foreach (var item in value.EnumerateArray())
            {
                if (!MatchesValue(item, items))
                {
                    return false;
                }
            }
        }

        return !RequiresUniqueItems(schema) || HasUniqueItems(value);
    }

    private static bool RequiresUniqueItems(JsonElement schema)
    {
        return schema.TryGetProperty("uniqueItems", out var uniqueItems)
            && uniqueItems.ValueKind == JsonValueKind.True;
    }

    private static bool HasUniqueItems(JsonElement array)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in array.EnumerateArray())
        {
            var key = item.ValueKind == JsonValueKind.String
                ? item.GetString() ?? string.Empty
                : item.GetRawText();
            if (!seen.Add(key))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasMissingRequired(IEnumerable<string> names, JsonElement schema)
    {
        if (!TryGetRequiredNames(schema, out var required))
        {
            return false;
        }

        var present = names as ISet<string> ?? new HashSet<string>(names, StringComparer.Ordinal);
        foreach (var name in required)
        {
            if (!present.Contains(name))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetDeclaredTypes(JsonElement schema, out IReadOnlyList<string> types)
    {
        if (schema.ValueKind == JsonValueKind.Object && schema.TryGetProperty("type", out var type))
        {
            if (type.ValueKind == JsonValueKind.String && type.GetString() is { } single)
            {
                types = [single];
                return true;
            }

            if (type.ValueKind == JsonValueKind.Array)
            {
                types = type.EnumerateArray()
                    .Select(value => value.GetString())
                    .OfType<string>()
                    .ToArray();
                return types.Count > 0;
            }
        }

        types = [];
        return false;
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

    private static bool JsonEquals(JsonElement left, JsonElement right)
    {
        if (left.ValueKind != right.ValueKind)
        {
            return false;
        }

        return left.ValueKind switch
        {
            JsonValueKind.String => string.Equals(left.GetString(), right.GetString(), StringComparison.Ordinal),
            JsonValueKind.Number => left.TryGetDecimal(out var leftNumber)
                && right.TryGetDecimal(out var rightNumber)
                && leftNumber == rightNumber,
            JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null => true,
            _ => string.Equals(left.GetRawText(), right.GetRawText(), StringComparison.Ordinal)
        };
    }
}
