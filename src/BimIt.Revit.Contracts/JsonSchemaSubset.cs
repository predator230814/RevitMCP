using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace BimIt.Revit.Contracts;

/// <summary>
/// Deterministic JSON Schema subset shared by MCP argument checks and ADR-0024 operation contracts.
/// Instance validation preserves the legacy Server behavior. Definition validation is the strict
/// operation-schema meta-schema and is not applied to existing MCP tool schemas.
/// </summary>
public static class JsonSchemaSubset
{
    private static readonly HashSet<string> SupportedTypeNames = new(StringComparer.Ordinal)
    {
        "object",
        "array",
        "string",
        "number",
        "integer",
        "boolean",
        "null"
    };

    private static readonly HashSet<string> AllowedKeywords = new(StringComparer.Ordinal)
    {
        "type",
        "enum",
        "const",
        "properties",
        "required",
        "additionalProperties",
        "items",
        "minLength",
        "maxLength",
        "minimum",
        "maximum",
        "minItems",
        "maxItems",
        "uniqueItems",
        "description",
        "title",
        "default"
    };

    public static bool MatchesArguments(IDictionary<string, JsonElement>? arguments, JsonElement schema)
    {
        return MatchesObject(arguments, schema);
    }

    public static bool MatchesInstance(JsonElement instance, JsonElement schema)
    {
        return TryMatchInstance(instance, schema, out _);
    }

    /// <summary>
    /// Strict instance check. <paramref name="error"/> names the first failing keyword and its path.
    /// This is the same recursive check as <see cref="MatchesInstance"/>.
    /// </summary>
    public static bool TryMatchInstance(
        JsonElement instance,
        JsonElement schema,
        [NotNullWhen(false)] out JsonSchemaInstanceError? error)
    {
        return TryMatchValue(instance, schema, strict: true, "$", out error);
    }

    public static bool HasUnexpectedProperties(IDictionary<string, JsonElement>? arguments, JsonElement schema)
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

    public static bool HasUnexpectedProperties(JsonElement value, JsonElement schema)
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

    public static bool TryValidateDefinition(
        JsonElement schema,
        [NotNullWhen(false)] out JsonSchemaDefinitionError? error)
    {
        return TryValidateNode(schema, "$", out error);
    }

    public static bool TryValidateOperationSchema(
        JsonElement schema,
        [NotNullWhen(false)] out JsonSchemaDefinitionError? error)
    {
        if (!TryValidateDefinition(schema, out error))
        {
            return false;
        }

        if (!schema.TryGetProperty("type", out var type)
            || type.ValueKind != JsonValueKind.String
            || !string.Equals(type.GetString(), "object", StringComparison.Ordinal))
        {
            error = new JsonSchemaDefinitionError("$", "type", "An operation schema root must declare type object.");
            return false;
        }

        return true;
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

            if (!TryMatchValue(value, property.Value, strict: false, "$." + property.Name, out _))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryMatchObjectElement(
        JsonElement value,
        JsonElement schema,
        bool strict,
        string path,
        out JsonSchemaInstanceError? error)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            return Mismatch(path, "type", out error);
        }

        if (IsClosedObject(schema))
        {
            var allowed = GetDeclaredNames(schema);
            foreach (var property in value.EnumerateObject())
            {
                if (!allowed.Contains(property.Name))
                {
                    return Mismatch(path + "." + property.Name, "additionalProperties", out error);
                }
            }
        }

        if (TryGetRequiredNames(schema, out var required))
        {
            var present = new HashSet<string>(EnumerateNames(value), StringComparer.Ordinal);
            foreach (var name in required)
            {
                if (!present.Contains(name))
                {
                    return Mismatch(path + "." + name, "required", out error);
                }
            }
        }

        if (!TryGetDeclaredProperties(schema, out var declared))
        {
            return Matched(out error);
        }

        foreach (var property in declared.EnumerateObject())
        {
            if (!value.TryGetProperty(property.Name, out var child))
            {
                continue;
            }

            if (!TryMatchValue(child, property.Value, strict, path + "." + property.Name, out error))
            {
                return false;
            }
        }

        return Matched(out error);
    }

    private static bool TryMatchValue(
        JsonElement value,
        JsonElement schema,
        bool strict,
        string path,
        out JsonSchemaInstanceError? error)
    {
        if (!MatchesDeclaredType(value, schema))
        {
            return Mismatch(path, "type", out error);
        }

        if (!strict && value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return Matched(out error);
        }

        if (!MatchesEnum(value, schema))
        {
            return Mismatch(path, "enum", out error);
        }

        if (!MatchesConst(value, schema))
        {
            return Mismatch(path, "const", out error);
        }

        if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return Matched(out error);
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => TryMatchString(value, schema, path, out error),
            JsonValueKind.Number => TryMatchNumber(value, schema, path, out error),
            JsonValueKind.Array => TryMatchArray(value, schema, strict, path, out error),
            JsonValueKind.Object => TryMatchObjectElement(value, schema, strict, path, out error),
            _ => Matched(out error)
        };
    }

    private static bool Matched(out JsonSchemaInstanceError? error)
    {
        error = null;
        return true;
    }

    private static bool Mismatch(string path, string keyword, out JsonSchemaInstanceError? error)
    {
        error = new JsonSchemaInstanceError(path, keyword);
        return false;
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

    private static bool TryMatchString(
        JsonElement value,
        JsonElement schema,
        string path,
        out JsonSchemaInstanceError? error)
    {
        var text = value.GetString() ?? string.Empty;
        if (schema.TryGetProperty("minLength", out var minLength)
            && minLength.TryGetInt32(out var min)
            && text.Length < min)
        {
            return Mismatch(path, "minLength", out error);
        }

        if (schema.TryGetProperty("maxLength", out var maxLength)
            && maxLength.TryGetInt32(out var max)
            && text.Length > max)
        {
            return Mismatch(path, "maxLength", out error);
        }

        return Matched(out error);
    }

    private static bool TryMatchNumber(
        JsonElement value,
        JsonElement schema,
        string path,
        out JsonSchemaInstanceError? error)
    {
        if (!value.TryGetDecimal(out var number))
        {
            return Mismatch(path, "type", out error);
        }

        if (schema.TryGetProperty("minimum", out var minimum)
            && minimum.TryGetDecimal(out var min)
            && number < min)
        {
            return Mismatch(path, "minimum", out error);
        }

        if (schema.TryGetProperty("maximum", out var maximum)
            && maximum.TryGetDecimal(out var max)
            && number > max)
        {
            return Mismatch(path, "maximum", out error);
        }

        return Matched(out error);
    }

    private static bool TryMatchArray(
        JsonElement value,
        JsonElement schema,
        bool strict,
        string path,
        out JsonSchemaInstanceError? error)
    {
        var length = value.GetArrayLength();
        if (schema.TryGetProperty("minItems", out var minItems)
            && minItems.TryGetInt32(out var min)
            && length < min)
        {
            return Mismatch(path, "minItems", out error);
        }

        if (schema.TryGetProperty("maxItems", out var maxItems)
            && maxItems.TryGetInt32(out var max)
            && length > max)
        {
            return Mismatch(path, "maxItems", out error);
        }

        if (schema.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Object)
        {
            var index = 0;
            foreach (var item in value.EnumerateArray())
            {
                if (!TryMatchValue(item, items, strict, path + "[" + index.ToString(System.Globalization.CultureInfo.InvariantCulture) + "]", out error))
                {
                    return false;
                }

                index++;
            }
        }

        if (RequiresUniqueItems(schema) && !HasUniqueItems(value))
        {
            return Mismatch(path, "uniqueItems", out error);
        }

        return Matched(out error);
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

    private static bool TryValidateNode(
        JsonElement schema,
        string path,
        [NotNullWhen(false)] out JsonSchemaDefinitionError? error)
    {
        if (schema.ValueKind != JsonValueKind.Object)
        {
            error = new JsonSchemaDefinitionError(path, "schema", "A schema node must be a JSON object.");
            return false;
        }

        if (!schema.TryGetProperty("type", out var typeElement))
        {
            error = new JsonSchemaDefinitionError(path, "type", "A schema node must declare type.");
            return false;
        }

        if (!TryReadDefinitionTypes(typeElement, out var types, out var typeMessage))
        {
            error = new JsonSchemaDefinitionError(path, "type", typeMessage);
            return false;
        }

        foreach (var property in schema.EnumerateObject())
        {
            if (!AllowedKeywords.Contains(property.Name))
            {
                error = new JsonSchemaDefinitionError(path, property.Name, "Keyword is not in the supported subset.");
                return false;
            }
        }

        var permitsObject = types.Contains("object");
        var permitsArray = types.Contains("array");
        var permitsString = types.Contains("string");
        var permitsNumber = types.Contains("number") || types.Contains("integer");

        if (!TryValidateKeywordApplicability(schema, path, permitsObject, permitsArray, permitsString, permitsNumber, out error))
        {
            return false;
        }

        if (!TryValidateKeywordShapes(schema, path, permitsObject, out error))
        {
            return false;
        }

        if (schema.TryGetProperty("properties", out var properties))
        {
            foreach (var property in properties.EnumerateObject())
            {
                if (!TryValidateNode(property.Value, path + "/properties/" + EscapePointer(property.Name), out error))
                {
                    return false;
                }
            }
        }

        if (schema.TryGetProperty("items", out var items)
            && !TryValidateNode(items, path + "/items", out error))
        {
            return false;
        }

        if (permitsObject
            && (!schema.TryGetProperty("additionalProperties", out var additional) || additional.ValueKind != JsonValueKind.False))
        {
            error = new JsonSchemaDefinitionError(
                path,
                "additionalProperties",
                "An object-capable schema must set additionalProperties to false.");
            return false;
        }

        error = null;
        return true;
    }

    private static bool TryValidateKeywordApplicability(
        JsonElement schema,
        string path,
        bool permitsObject,
        bool permitsArray,
        bool permitsString,
        bool permitsNumber,
        [NotNullWhen(false)] out JsonSchemaDefinitionError? error)
    {
        if (!permitsObject && HasAny(schema, "properties", "required", "additionalProperties"))
        {
            error = new JsonSchemaDefinitionError(path, FirstPresent(schema, "properties", "required", "additionalProperties"), "Object keywords require a type that permits object.");
            return false;
        }

        if (!permitsArray && HasAny(schema, "items", "minItems", "maxItems", "uniqueItems"))
        {
            error = new JsonSchemaDefinitionError(path, FirstPresent(schema, "items", "minItems", "maxItems", "uniqueItems"), "Array keywords require a type that permits array.");
            return false;
        }

        if (!permitsString && HasAny(schema, "minLength", "maxLength"))
        {
            error = new JsonSchemaDefinitionError(path, FirstPresent(schema, "minLength", "maxLength"), "String-length keywords require a type that permits string.");
            return false;
        }

        if (!permitsNumber && HasAny(schema, "minimum", "maximum"))
        {
            error = new JsonSchemaDefinitionError(path, FirstPresent(schema, "minimum", "maximum"), "Numeric bounds require a type that permits integer or number.");
            return false;
        }

        error = null;
        return true;
    }

    private static bool TryValidateKeywordShapes(
        JsonElement schema,
        string path,
        bool permitsObject,
        [NotNullWhen(false)] out JsonSchemaDefinitionError? error)
    {
        if (schema.TryGetProperty("properties", out var properties) && properties.ValueKind != JsonValueKind.Object)
        {
            error = new JsonSchemaDefinitionError(path, "properties", "properties must be a JSON object.");
            return false;
        }

        if (properties.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in properties.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.Object)
                {
                    error = new JsonSchemaDefinitionError(
                        path + "/properties/" + EscapePointer(property.Name),
                        "properties",
                        "A property schema must be a JSON object.");
                    return false;
                }
            }
        }

        if (schema.TryGetProperty("required", out var required))
        {
            if (required.ValueKind != JsonValueKind.Array)
            {
                error = new JsonSchemaDefinitionError(path, "required", "required must be an array of strings.");
                return false;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var declared = new HashSet<string>(StringComparer.Ordinal);
            if (properties.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in properties.EnumerateObject())
                {
                    declared.Add(property.Name);
                }
            }

            foreach (var name in required.EnumerateArray())
            {
                if (name.ValueKind != JsonValueKind.String || name.GetString() is not { } text)
                {
                    error = new JsonSchemaDefinitionError(path, "required", "required must contain strings.");
                    return false;
                }
                if (!seen.Add(text))
                {
                    error = new JsonSchemaDefinitionError(path, "required", "required names must be unique.");
                    return false;
                }

                if (!declared.Contains(text))
                {
                    error = new JsonSchemaDefinitionError(path, "required", "A required name must be declared in properties.");
                    return false;
                }
            }
        }

        if (permitsObject && schema.TryGetProperty("additionalProperties", out var additional) && additional.ValueKind != JsonValueKind.False)
        {
            error = new JsonSchemaDefinitionError(path, "additionalProperties", "additionalProperties must be false.");
            return false;
        }

        if (schema.TryGetProperty("items", out var items) && items.ValueKind != JsonValueKind.Object)
        {
            error = new JsonSchemaDefinitionError(path, "items", "items must be one schema object.");
            return false;
        }

        if (!TryValidateIntegerBoundPair(schema, path, "minLength", "maxLength", out error)
            || !TryValidateIntegerBoundPair(schema, path, "minItems", "maxItems", out error)
            || !TryValidateNumberBoundPair(schema, path, out error))
        {
            return false;
        }

        if (schema.TryGetProperty("uniqueItems", out var uniqueItems)
            && uniqueItems.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            error = new JsonSchemaDefinitionError(path, "uniqueItems", "uniqueItems must be a boolean.");
            return false;
        }

        if (schema.TryGetProperty("enum", out var enumeration) && enumeration.ValueKind != JsonValueKind.Array)
        {
            error = new JsonSchemaDefinitionError(path, "enum", "enum must be a JSON array.");
            return false;
        }

        if (schema.TryGetProperty("description", out var description) && description.ValueKind != JsonValueKind.String)
        {
            error = new JsonSchemaDefinitionError(path, "description", "description must be a string.");
            return false;
        }

        if (schema.TryGetProperty("title", out var title) && title.ValueKind != JsonValueKind.String)
        {
            error = new JsonSchemaDefinitionError(path, "title", "title must be a string.");
            return false;
        }

        error = null;
        return true;
    }

    private static bool TryValidateIntegerBoundPair(
        JsonElement schema,
        string path,
        string minimumName,
        string maximumName,
        [NotNullWhen(false)] out JsonSchemaDefinitionError? error)
    {
        int? minimum = null;
        int? maximum = null;
        if (schema.TryGetProperty(minimumName, out var minimumElement))
        {
            if (!TryGetNonNegativeInt(minimumElement, out var value))
            {
                error = new JsonSchemaDefinitionError(path, minimumName, minimumName + " must be a non-negative integer.");
                return false;
            }

            minimum = value;
        }

        if (schema.TryGetProperty(maximumName, out var maximumElement))
        {
            if (!TryGetNonNegativeInt(maximumElement, out var value))
            {
                error = new JsonSchemaDefinitionError(path, maximumName, maximumName + " must be a non-negative integer.");
                return false;
            }

            maximum = value;
        }

        if (minimum is { } min && maximum is { } max && min > max)
        {
            error = new JsonSchemaDefinitionError(path, minimumName, minimumName + " must be less than or equal to " + maximumName + ".");
            return false;
        }

        error = null;
        return true;
    }

    private static bool TryValidateNumberBoundPair(
        JsonElement schema,
        string path,
        [NotNullWhen(false)] out JsonSchemaDefinitionError? error)
    {
        decimal? minimum = null;
        decimal? maximum = null;
        if (schema.TryGetProperty("minimum", out var minimumElement))
        {
            if (minimumElement.ValueKind != JsonValueKind.Number || !minimumElement.TryGetDecimal(out var value))
            {
                error = new JsonSchemaDefinitionError(path, "minimum", "minimum must be a JSON number.");
                return false;
            }

            minimum = value;
        }

        if (schema.TryGetProperty("maximum", out var maximumElement))
        {
            if (maximumElement.ValueKind != JsonValueKind.Number || !maximumElement.TryGetDecimal(out var value))
            {
                error = new JsonSchemaDefinitionError(path, "maximum", "maximum must be a JSON number.");
                return false;
            }

            maximum = value;
        }

        if (minimum is { } min && maximum is { } max && min > max)
        {
            error = new JsonSchemaDefinitionError(path, "minimum", "minimum must be less than or equal to maximum.");
            return false;
        }

        error = null;
        return true;
    }

    private static bool TryReadDefinitionTypes(JsonElement type, out string[] types, out string message)
    {
        if (type.ValueKind == JsonValueKind.String)
        {
            var name = type.GetString() ?? string.Empty;
            if (!SupportedTypeNames.Contains(name))
            {
                types = [];
                message = "Type name is not supported.";
                return false;
            }

            types = [name];
            message = string.Empty;
            return true;
        }

        if (type.ValueKind != JsonValueKind.Array)
        {
            types = [];
            message = "Type must be a string or an array of strings.";
            return false;
        }

        if (type.GetArrayLength() == 0)
        {
            types = [];
            message = "Type array must not be empty.";
            return false;
        }

        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in type.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                types = [];
                message = "Type array must contain only strings.";
                return false;
            }

            var name = item.GetString() ?? string.Empty;
            if (!SupportedTypeNames.Contains(name))
            {
                types = [];
                message = "Type name is not supported.";
                return false;
            }

            if (!seen.Add(name))
            {
                types = [];
                message = "Type array must not contain duplicates.";
                return false;
            }

            names.Add(name);
        }

        types = names.ToArray();
        message = string.Empty;
        return true;
    }

    private static bool TryGetNonNegativeInt(JsonElement value, out int number)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out number) && number >= 0)
        {
            return true;
        }

        number = 0;
        return false;
    }

    private static bool HasAny(JsonElement schema, params string[] names)
    {
        foreach (var name in names)
        {
            if (schema.TryGetProperty(name, out _))
            {
                return true;
            }
        }

        return false;
    }

    private static string FirstPresent(JsonElement schema, params string[] names)
    {
        foreach (var name in names)
        {
            if (schema.TryGetProperty(name, out _))
            {
                return name;
            }
        }

        return names[0];
    }

    private static string EscapePointer(string name)
    {
        return name.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
    }
}

public sealed record JsonSchemaDefinitionError(string Path, string Keyword, string Message);

/// <summary>First strict-instance failure. The path does not include the rejected value.</summary>
public sealed record JsonSchemaInstanceError(string Path, string Keyword);
