using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace BimIt.Revit.Addin.CSharpQuery;

internal readonly record struct CSharpQueryCopiedResult(object? Value, string Json);

internal sealed class CSharpQueryCopyException : Exception
{
    public CSharpQueryCopyException(string code)
    {
        Code = code;
    }

    public string Code { get; }
}

internal static class CSharpQueryResultCopier
{
    public static CSharpQueryCopiedResult Copy(object? value, Assembly? generatedAssembly)
    {
        var budget = new NodeBudget();
        var host = CopyValue(value, generatedAssembly, depth: 1, budget);
        string json;
        try
        {
            json = JsonSerializer.Serialize(host);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            throw new CSharpQueryCopyException(CSharpQueryCodes.InvalidResult);
        }

        if (json.Length > CSharpQueryLimits.MaxSerializedCharacters)
        {
            throw new CSharpQueryCopyException(CSharpQueryCodes.ResultTooLarge);
        }

        return new CSharpQueryCopiedResult(host, json);
    }

    private static object? CopyValue(object? value, Assembly? generatedAssembly, int depth, NodeBudget budget)
    {
        if (depth > CSharpQueryLimits.MaxDepth)
        {
            throw new CSharpQueryCopyException(CSharpQueryCodes.ResultTooLarge);
        }

        if (value is null)
        {
            budget.Add();
            return null;
        }

        if (value is string or bool or byte or sbyte or short or ushort or int or uint or long or ulong or decimal or char)
        {
            budget.Add();
            return value;
        }

        if (value is float number)
        {
            if (!float.IsFinite(number))
            {
                throw new CSharpQueryCopyException(CSharpQueryCodes.InvalidResult);
            }

            budget.Add();
            return number;
        }

        if (value is double real)
        {
            if (!double.IsFinite(real))
            {
                throw new CSharpQueryCopyException(CSharpQueryCodes.InvalidResult);
            }

            budget.Add();
            return real;
        }

        var type = value.GetType();
        if (type.IsEnum || IsRejectedRuntimeType(type))
        {
            throw new CSharpQueryCopyException(CSharpQueryCodes.InvalidResult);
        }

        if (type.IsSZArray)
        {
            return CopyArray((Array)value, generatedAssembly, depth, budget);
        }

        if (type.IsArray)
        {
            throw new CSharpQueryCopyException(CSharpQueryCodes.InvalidResult);
        }

        if (IsExactList(type))
        {
            return CopyList((IList)value, generatedAssembly, depth, budget);
        }

        if (IsExactStringDictionary(type))
        {
            return CopyDictionary((IDictionary)value, generatedAssembly, depth, budget);
        }

        if (IsAnonymousProjection(type, generatedAssembly))
        {
            return CopyAnonymous(value, type, generatedAssembly!, depth, budget);
        }

        throw new CSharpQueryCopyException(CSharpQueryCodes.InvalidResult);
    }

    private static object?[] CopyArray(Array array, Assembly? generatedAssembly, int depth, NodeBudget budget)
    {
        var items = new object?[array.Length];
        for (var index = 0; index < array.Length; index++)
        {
            items[index] = CopyValue(array.GetValue(index), generatedAssembly, depth + 1, budget);
        }

        return items;
    }

    private static object?[] CopyList(IList list, Assembly? generatedAssembly, int depth, NodeBudget budget)
    {
        var items = new object?[list.Count];
        for (var index = 0; index < list.Count; index++)
        {
            items[index] = CopyValue(list[index], generatedAssembly, depth + 1, budget);
        }

        return items;
    }

    private static SortedDictionary<string, object?> CopyDictionary(
        IDictionary dictionary,
        Assembly? generatedAssembly,
        int depth,
        NodeBudget budget)
    {
        var keys = new string[dictionary.Count];
        var copy = dictionary.Keys.GetType().GetMethod("CopyTo", [typeof(string[]), typeof(int)])
            ?? throw new CSharpQueryCopyException(CSharpQueryCodes.InvalidResult);
        copy.Invoke(dictionary.Keys, [keys, 0]);
        Array.Sort(keys, StringComparer.Ordinal);
        var map = new SortedDictionary<string, object?>(StringComparer.Ordinal);
        foreach (var key in keys)
        {
            map[key] = CopyValue(dictionary[key], generatedAssembly, depth + 1, budget);
        }

        return map;
    }

    private static SortedDictionary<string, object?> CopyAnonymous(
        object value,
        Type type,
        Assembly generatedAssembly,
        int depth,
        NodeBudget budget)
    {
        var properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.GetMethod is { IsPublic: true } && property.GetIndexParameters().Length == 0)
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .ToArray();
        var map = new SortedDictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in properties)
        {
            var propertyValue = property.GetMethod!.Invoke(value, null);
            map[property.Name] = CopyValue(propertyValue, generatedAssembly, depth + 1, budget);
        }

        return map;
    }

    private static bool IsExactList(Type type)
    {
        return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>);
    }

    private static bool IsExactStringDictionary(Type type)
    {
        return type.IsGenericType
            && type.GetGenericTypeDefinition() == typeof(Dictionary<,>)
            && type.GetGenericArguments()[0] == typeof(string);
    }

    private static bool IsAnonymousProjection(Type type, Assembly? generatedAssembly)
    {
        return generatedAssembly is not null
            && type.Assembly == generatedAssembly
            && type.IsClass
            && type.IsSealed
            && !type.IsAbstract
            && type.Name.Contains("AnonymousType", StringComparison.Ordinal)
            && type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false);
    }

    private static bool IsRejectedRuntimeType(Type type)
    {
        if (typeof(Delegate).IsAssignableFrom(type)
            || typeof(MemberInfo).IsAssignableFrom(type)
            || typeof(Type).IsAssignableFrom(type)
            || typeof(Assembly).IsAssignableFrom(type))
        {
            return true;
        }

        var space = type.Namespace ?? "";
        return space.Equals("Autodesk.Revit.DB", StringComparison.Ordinal)
            || space.StartsWith("Autodesk.Revit.", StringComparison.Ordinal);
    }

    private sealed class NodeBudget
    {
        public int Count { get; private set; }

        public void Add()
        {
            Count++;
            if (Count > CSharpQueryLimits.MaxNodes)
            {
                throw new CSharpQueryCopyException(CSharpQueryCodes.ResultTooLarge);
            }
        }
    }
}
