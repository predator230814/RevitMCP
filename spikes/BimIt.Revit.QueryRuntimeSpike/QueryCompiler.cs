using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace BimIt.Revit.QueryRuntimeSpike;

public sealed class QueryCompilation
{
    private QueryCompilation(
        bool success,
        string cacheKey,
        byte[]? assembly,
        IReadOnlyList<QueryDiagnostic> diagnostics)
    {
        Success = success;
        CacheKey = cacheKey;
        Assembly = assembly;
        Diagnostics = diagnostics;
    }

    public bool Success { get; }

    public string CacheKey { get; }

    public byte[]? Assembly { get; }

    public IReadOnlyList<QueryDiagnostic> Diagnostics { get; }

    internal static QueryCompilation Failed(string cacheKey, IReadOnlyList<QueryDiagnostic> diagnostics)
    {
        return new QueryCompilation(false, cacheKey, null, diagnostics);
    }

    internal static QueryCompilation Succeeded(string cacheKey, byte[] assembly)
    {
        return new QueryCompilation(true, cacheKey, assembly, []);
    }
}

public sealed class QueryCompiler
{
    private readonly string _contract;
    private readonly string? _revitApiPath;

    private QueryCompiler(string contract, RevitApiIdentity identity, string? revitApiPath)
    {
        _contract = contract;
        ApiIdentity = identity;
        _revitApiPath = revitApiPath;
    }

    public RevitApiIdentity ApiIdentity { get; }

    public static QueryCompiler ForRevitApi(string revitApiPath)
    {
        return new QueryCompiler(
            QueryPolicy.ContractRevitDocument,
            RevitApiIdentity.FromFile(revitApiPath),
            revitApiPath);
    }

    public static QueryCompiler ForHostProbe()
    {
        return new QueryCompiler(QueryPolicy.ContractHostProbe, RevitApiIdentity.HostProbe, null);
    }

    public string KeyFor(string source)
    {
        return QueryCacheKey.Create(source, _contract, ApiIdentity, QueryPolicy.Version);
    }

    public QueryCompilation Compile(string source)
    {
        var cacheKey = KeyFor(source);
        if (string.IsNullOrWhiteSpace(source))
        {
            return QueryCompilation.Failed(cacheKey, [QueryDiagnostic.Error("BIMQ100", 0, 0, "source is empty")]);
        }

        if (source.Length > QueryPolicy.MaxSourceCharacters)
        {
            return QueryCompilation.Failed(cacheKey, [QueryDiagnostic.Error("BIMQ100", 0, 0, "source exceeds the size bound")]);
        }

        if (ContainsHostEscape(source, out var escape))
        {
            return QueryCompilation.Failed(cacheKey, [QueryDiagnostic.Error("BIMQ110", 1, 1, escape)]);
        }

        var tree = CSharpSyntaxTree.ParseText(Wrap(source), encoding: Encoding.UTF8, path: "generated.cs");
        tree = LoopBudgetRewriter.Rewrite(tree);
        var compilation = CSharpCompilation.Create(
            "BimIt.Revit.QueryRuntimeSpike.Generated",
            [tree],
            CompilationReferences.Create(_revitApiPath),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                deterministic: true,
                allowUnsafe: false,
                nullableContextOptions: NullableContextOptions.Disable));

        var policy = ForbiddenApiAnalyzer.Analyze(compilation, tree);
        var errors = DiagnosticList.FromCompiler(compilation.GetDiagnostics());
        if (policy.Count > 0 || errors.Count > 0)
        {
            return QueryCompilation.Failed(cacheKey, DiagnosticList.Merge(policy, errors));
        }

        using var pe = new MemoryStream();
        var emitted = compilation.Emit(pe);
        if (!emitted.Success)
        {
            return QueryCompilation.Failed(
                cacheKey,
                DiagnosticList.Merge(DiagnosticList.FromCompiler(emitted.Diagnostics)));
        }

        return QueryCompilation.Succeeded(cacheKey, pe.ToArray());
    }

    private string Wrap(string source)
    {
        var builder = new StringBuilder();
        builder.AppendLine("using System;");
        builder.AppendLine("using System.Collections.Generic;");
        builder.AppendLine("using System.Linq;");
        if (_contract == QueryPolicy.ContractRevitDocument)
        {
            builder.AppendLine("using Autodesk.Revit.DB;");
        }

        builder.AppendLine("using BimIt.Revit.QueryRuntimeSpike;");
        builder.AppendLine();
        builder.AppendLine("namespace BimIt.Revit.QueryRuntimeSpike.Generated");
        builder.AppendLine("{");
        builder.AppendLine("    public static class GeneratedQuery");
        builder.AppendLine("    {");
        builder.Append("        public static object Execute(");
        builder.Append(_contract == QueryPolicy.ContractRevitDocument
            ? "Document doc, System.Func<Element, string> elementRef, QueryBudget __budget"
            : "object doc, System.Func<object, string> elementRef, QueryBudget __budget");
        builder.AppendLine(")");
        builder.AppendLine("        {");
        builder.AppendLine("#line 1 \"query.cs\"");
        builder.AppendLine(source);
        builder.AppendLine("#line default");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static bool ContainsHostEscape(string source, out string message)
    {
        if (source.Contains("__budget", StringComparison.Ordinal) || source.Contains("QueryBudget", StringComparison.Ordinal))
        {
            message = "the execution budget is host-owned";
            return true;
        }

        foreach (var line in source.Split('\n'))
        {
            if (line.TrimStart().StartsWith('#'))
            {
                message = "preprocessor directives are outside the read-only query policy";
                return true;
            }
        }

        message = "";
        return false;
    }
}
