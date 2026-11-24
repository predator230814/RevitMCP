namespace BimIt.Revit.QueryRuntimeSpike;

public sealed class QueryCompilationCache
{
    private readonly Dictionary<string, QueryCompilation> _compiled = new(StringComparer.Ordinal);

    public int Misses { get; private set; }

    public QueryCompilation GetOrCompile(QueryCompiler compiler, string source)
    {
        ArgumentNullException.ThrowIfNull(compiler);
        var key = compiler.KeyFor(source);
        if (_compiled.TryGetValue(key, out var existing))
        {
            return existing;
        }

        Misses++;
        var compiled = compiler.Compile(source);
        _compiled[compiled.CacheKey] = compiled;
        return compiled;
    }
}
