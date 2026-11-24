using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BimIt.Revit.QueryRuntimeSpike;

public sealed class QueryRunResult
{
    public required bool Success { get; init; }

    public required string? Json { get; init; }

    public required string CacheKey { get; init; }

    public required IReadOnlyList<QueryDiagnostic> Diagnostics { get; init; }
}

public sealed class QueryExecutionEvidence
{
    public required QueryRunResult Result { get; init; }

    public WeakReference? Context { get; init; }
}

public static class QueryExecutor
{
    public static QueryExecutionEvidence Execute(
        QueryCompilation compilation,
        DocumentReadiness readiness,
        object? document,
        Delegate elementRef,
        QueryBudget budget)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        ArgumentNullException.ThrowIfNull(elementRef);
        ArgumentNullException.ThrowIfNull(budget);

        if (!compilation.Success || compilation.Assembly is null)
        {
            return Evidence(compilation.CacheKey, false, null, compilation.Diagnostics, null);
        }

        var rejection = readiness.RejectionCode();
        if (rejection is not null)
        {
            return Evidence(
                compilation.CacheKey,
                false,
                null,
                [QueryDiagnostic.Error("BIMQ130", 0, 0, rejection)],
                null);
        }

        return ExecuteCollectible(compilation, document, elementRef, budget);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static QueryExecutionEvidence ExecuteCollectible(
        QueryCompilation compilation,
        object? document,
        Delegate elementRef,
        QueryBudget budget)
    {
        var context = new QueryAssemblyContext();
        var reference = new WeakReference(context, trackResurrection: false);
        try
        {
            var json = Invoke(context, compilation.Assembly!, document, elementRef, budget);
            return Evidence(compilation.CacheKey, true, json, [], reference);
        }
        catch (QueryBudgetExceededException ex)
        {
            return Evidence(
                compilation.CacheKey,
                false,
                null,
                [QueryDiagnostic.Error("BIMQ120", 0, 0, ex.Message)],
                reference);
        }
        catch (QueryResultException ex)
        {
            return Evidence(
                compilation.CacheKey,
                false,
                null,
                [QueryDiagnostic.Error(ex.Code, 0, 0, ex.Message)],
                reference);
        }
        catch (QueryFailedException ex)
        {
            return Evidence(
                compilation.CacheKey,
                false,
                null,
                [QueryDiagnostic.Error("BIMQ140", 0, 0, ex.Failure)],
                reference);
        }
        finally
        {
            context.Unload();
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string Invoke(
        QueryAssemblyContext context,
        byte[] assemblyBytes,
        object? document,
        Delegate elementRef,
        QueryBudget budget)
    {
        Assembly assembly;
        Type type;
        MethodInfo method;
        try
        {
            assembly = context.LoadGenerated(assemblyBytes);
            type = assembly.GetType("BimIt.Revit.QueryRuntimeSpike.Generated.GeneratedQuery", throwOnError: true)
                ?? throw new QueryFailedException("MissingType", "GeneratedQuery");
            method = type.GetMethod("Execute", BindingFlags.Public | BindingFlags.Static)
                ?? throw new QueryFailedException("MissingMethod", "Execute");
        }
        catch (QueryFailedException)
        {
            throw;
        }
        catch (Exception ex) when (ex is FileNotFoundException or FileLoadException or BadImageFormatException or TypeLoadException or ReflectionTypeLoadException)
        {
            throw new QueryFailedException(ex.GetType().Name, ex.Message);
        }
        object? value;
        try
        {
            value = method.Invoke(null, [document, elementRef, budget]);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is QueryBudgetExceededException budgetExceeded)
        {
            throw new QueryBudgetExceededException(budgetExceeded.Limit);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is QueryResultException result)
        {
            throw result;
        }
        catch (TargetInvocationException ex)
        {
            var inner = ex.InnerException;
            throw new QueryFailedException(inner?.GetType().Name ?? "Exception", inner?.Message ?? ex.Message);
        }
        catch (Exception ex) when (ex is ArgumentException or FileNotFoundException or FileLoadException or BadImageFormatException or TypeLoadException or ReflectionTypeLoadException)
        {
            throw new QueryFailedException(ex.GetType().Name, ex.Message);
        }

        return QueryResultSerializer.Serialize(value);
    }

    private static QueryExecutionEvidence Evidence(
        string cacheKey,
        bool success,
        string? json,
        IReadOnlyList<QueryDiagnostic> diagnostics,
        WeakReference? context)
    {
        return new QueryExecutionEvidence
        {
            Result = new QueryRunResult
            {
                Success = success,
                Json = json,
                CacheKey = cacheKey,
                Diagnostics = diagnostics
            },
            Context = context
        };
    }
}

public sealed class QueryAssemblyContext : AssemblyLoadContext
{
    public QueryAssemblyContext()
        : base(isCollectible: true)
    {
    }

    public Assembly LoadGenerated(byte[] assemblyBytes)
    {
        return LoadFromStream(new MemoryStream(assemblyBytes, writable: false));
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        return null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        return IntPtr.Zero;
    }
}

internal static class QueryResultSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        MaxDepth = 8,
        ReferenceHandler = ReferenceHandler.IgnoreCycles
    };

    public static string Serialize(object? value)
    {
        if (value is not null)
        {
            var space = value.GetType().Namespace ?? "";
            if (space.StartsWith("Autodesk.Revit", StringComparison.Ordinal))
            {
                throw new QueryResultException("BIMQ140", "result must be JSON data");
            }
        }

        try
        {
            var json = JsonSerializer.Serialize(Materialize(value, 0), Options);
            if (json.Length > QueryPolicy.MaxResultCharacters)
            {
                throw new QueryResultException("BIMQ101", "result exceeds the size bound");
            }

            return json;
        }
        catch (QueryResultException)
        {
            throw;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            throw new QueryResultException("BIMQ140", "result must be JSON data");
        }
    }

    private static object? Materialize(object? value, int depth)
    {
        if (value is null || value is string || value is bool || value is byte || value is sbyte
            || value is short || value is ushort || value is int || value is uint
            || value is long || value is ulong || value is float || value is double || value is decimal
            || value is char)
        {
            return value;
        }

        if (depth > 8)
        {
            throw new QueryResultException("BIMQ140", "result exceeds the depth bound");
        }

        var space = value.GetType().Namespace ?? "";
        if (space.StartsWith("Autodesk.Revit", StringComparison.Ordinal)
            || space.StartsWith("BimIt.Revit.QueryRuntimeSpike.Generated", StringComparison.Ordinal))
        {
            if (value is not System.Collections.IEnumerable)
            {
                throw new QueryResultException("BIMQ140", "result must be JSON data");
            }
        }

        if (value is System.Collections.IDictionary dictionary)
        {
            var map = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (System.Collections.DictionaryEntry entry in dictionary)
            {
                map[Convert.ToString(entry.Key, System.Globalization.CultureInfo.InvariantCulture) ?? ""] =
                    Materialize(entry.Value, depth + 1);
            }

            return map;
        }

        if (value is System.Collections.IEnumerable enumerable)
        {
            var list = new List<object?>();
            foreach (var item in enumerable)
            {
                list.Add(Materialize(item, depth + 1));
            }

            return list;
        }

        if (space.StartsWith("Autodesk.Revit", StringComparison.Ordinal)
            || space.StartsWith("BimIt.Revit.QueryRuntimeSpike.Generated", StringComparison.Ordinal))
        {
            throw new QueryResultException("BIMQ140", "result must be JSON data");
        }

        var properties = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in value.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (property.GetIndexParameters().Length > 0 || property.GetMethod is null)
            {
                continue;
            }

            properties[property.Name] = Materialize(property.GetValue(value), depth + 1);
        }

        return properties;
    }
}

internal sealed class QueryResultException : Exception
{
    public QueryResultException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}

internal sealed class QueryFailedException : Exception
{
    public QueryFailedException(string typeName, string message)
        : base(typeName + ": " + message)
    {
        Failure = typeName + ": " + Flatten(message);
    }

    public string Failure { get; }

    private static string Flatten(string message)
    {
        var flat = message.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal).Trim();
        return flat.Length <= 160 ? flat : flat[..160];
    }
}
