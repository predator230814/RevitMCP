using System.Text;
using BimIt.Revit.Compiler;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BimIt.Revit.CompilerHost;

public static class CSharpCompilationPipeline
{
    public static CompilerResponse Compile(CompilerRequest request)
    {
        return CompileCore(request, testManifest: false);
    }

    internal static CompilerResponse CompileForTestManifest(CompilerRequest request)
    {
        return CompileCore(request, testManifest: true);
    }

    internal static string InstrumentForTests(string source)
    {
        var tree = LoopChargeRewriter.Rewrite(Parse(Wrap(source)));
        return tree.ToString();
    }

    private static CompilerResponse CompileCore(CompilerRequest request, bool testManifest)
    {
        ArgumentNullException.ThrowIfNull(request);
        var identity = request.RevitApi;
        var hash = CompilerHashes.SourceSha256(request.Source ?? "");
        var cacheKey = CompilerHashes.CacheKey(request.Source ?? "", identity, CompilerProtocol.PolicyVersion);
        if (request.ContractVersion != CompilerProtocol.ContractVersion
            || request.PolicyVersion != CompilerProtocol.PolicyVersion)
        {
            return Reject(CompilerOutcomes.InvalidRequest, CompilerResultCodes.InvalidSource, hash, cacheKey, identity, "compiler contract is not supported");
        }

        if (string.IsNullOrWhiteSpace(request.Source) || request.Source.Length > CompilerProtocol.MaxSourceCharacters)
        {
            return Reject(CompilerOutcomes.InvalidRequest, CompilerResultCodes.InvalidSource, hash, cacheKey, identity, "source is outside the size bound");
        }

        if (!testManifest && !identity.IsPolicyV1Supported)
        {
            return Reject(CompilerOutcomes.ApiUnsupported, CompilerResultCodes.ApiUnsupported, hash, cacheKey, identity, "loaded Revit API identity is not supported");
        }

        if (!identity.Name.Equals(CompilerProtocol.SupportedApiName, StringComparison.Ordinal))
        {
            return Reject(CompilerOutcomes.ApiUnsupported, CompilerResultCodes.ApiUnsupported, hash, cacheKey, identity, "loaded Revit API identity is not supported");
        }

        RevitApiIdentity fileIdentity;
        try
        {
            fileIdentity = RevitApiIdentity.FromFile(request.RevitApiPath);
        }
        catch (Exception)
        {
            return Reject(CompilerOutcomes.IdentityRejected, CompilerResultCodes.CompilerFailed, hash, cacheKey, identity, "Revit API identity was rejected");
        }

        if (!fileIdentity.Matches(identity))
        {
            return Reject(CompilerOutcomes.IdentityRejected, CompilerResultCodes.CompilerFailed, hash, cacheKey, identity, "Revit API identity was rejected");
        }

        if (SourceRejected(request.Source, out var envelope))
        {
            return Reject(CompilerOutcomes.InvalidRequest, CompilerResultCodes.InvalidSource, hash, cacheKey, identity, envelope);
        }

        SyntaxTree tree;
        CSharpCompilation compilation;
        try
        {
            tree = LoopChargeRewriter.Rewrite(Parse(Wrap(request.Source)));
            if (!HasClosedShape(tree))
            {
                return Reject(CompilerOutcomes.InvalidRequest, CompilerResultCodes.InvalidSource, hash, cacheKey, identity, "source is not a method body");
            }

            compilation = CSharpCompilation.Create(
                "BimIt.Revit.GeneratedQuery",
                [tree],
                CompilationReferences.Create(request.RevitApiPath),
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    optimizationLevel: OptimizationLevel.Release,
                    deterministic: true,
                    allowUnsafe: false,
                    nullableContextOptions: NullableContextOptions.Disable));
        }
        catch (ReferencePackUnavailableException)
        {
            return Reject(CompilerOutcomes.CompileFailed, CompilerResultCodes.CompileFailed, hash, cacheKey, identity, "packaged reference assemblies are unavailable");
        }
        catch (Exception)
        {
            return Reject(CompilerOutcomes.CompileFailed, CompilerResultCodes.CompileFailed, hash, cacheKey, identity, "compilation failed");
        }

        var policy = QueryPolicyAnalyzer.Analyze(compilation, tree);
        var errors = CompilerDiagnosticList.FromCompiler(compilation.GetDiagnostics());
        if (policy.Count > 0 || errors.Count > 0)
        {
            return Failed(
                policy.Count > 0 ? CompilerOutcomes.PolicyRejected : CompilerOutcomes.CompileFailed,
                hash,
                cacheKey,
                identity,
                CompilerDiagnosticList.Merge(policy, errors));
        }

        using var pe = new MemoryStream();
        var emitted = compilation.Emit(pe);
        if (!emitted.Success)
        {
            return Failed(
                CompilerOutcomes.CompileFailed,
                hash,
                cacheKey,
                identity,
                CompilerDiagnosticList.Merge(CompilerDiagnosticList.FromCompiler(emitted.Diagnostics)));
        }

        return new CompilerResponse
        {
            ContractVersion = CompilerProtocol.ContractVersion,
            Outcome = CompilerOutcomes.Success,
            PolicyVersion = CompilerProtocol.PolicyVersion,
            SourceSha256 = hash,
            CacheKey = cacheKey,
            RevitApi = identity,
            Diagnostics = [],
            Pe = pe.ToArray()
        };
    }

    private static bool SourceRejected(string source, out string message)
    {
        if (source.Contains(CompilerProtocol.ReservedPrefix, StringComparison.Ordinal))
        {
            message = "a reserved compiler identifier is outside the read-only query policy";
            return true;
        }

        foreach (var raw in source.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith('#'))
            {
                message = "preprocessor directives are outside the read-only query policy";
                return true;
            }

            if (line.StartsWith("using ", StringComparison.Ordinal)
                && line.EndsWith(';')
                && !line.StartsWith("using var ", StringComparison.Ordinal)
                && !line.Contains('('))
            {
                message = "using directives are outside the read-only query policy";
                return true;
            }
        }

        message = "";
        return false;
    }

    private static bool HasClosedShape(SyntaxTree tree)
    {
        var types = tree.GetRoot().DescendantNodes().OfType<TypeDeclarationSyntax>().ToList();
        if (types.Count != 1 || types[0].Identifier.ValueText != CompilerProtocol.GeneratedTypeName)
        {
            return false;
        }

        var methods = types[0].Members.OfType<MethodDeclarationSyntax>().ToList();
        return methods.Count == 1 && methods[0].Identifier.ValueText == CompilerProtocol.GeneratedMethodName;
    }

    private static SyntaxTree Parse(string source)
    {
        return CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.Latest),
            path: "wrapper.cs",
            encoding: Encoding.UTF8);
    }

    private static string Wrap(string source)
    {
        var builder = new StringBuilder();
        builder.AppendLine("using System;");
        builder.AppendLine("using System.Collections.Generic;");
        builder.AppendLine("using System.Linq;");
        builder.AppendLine("using Autodesk.Revit.DB;");
        builder.AppendLine();
        builder.Append("namespace ").AppendLine(CompilerProtocol.GeneratedNamespace);
        builder.AppendLine("{");
        builder.Append("    public static class ").AppendLine(CompilerProtocol.GeneratedTypeName);
        builder.AppendLine("    {");
        builder.Append("        public static object ").Append(CompilerProtocol.GeneratedMethodName).Append("(Document doc, System.Func<Element, string> elementRef, System.Action ").Append(CompilerProtocol.ChargeIdentifier).AppendLine(")");
        builder.AppendLine("        {");
        builder.Append("#line 1 \"").Append(CompilerProtocol.QueryPath).AppendLine("\"");
        builder.AppendLine(source);
        builder.AppendLine("#line default");
        builder.AppendLine("#line hidden");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static CompilerResponse Reject(
        string outcome,
        string code,
        string hash,
        string cacheKey,
        RevitApiIdentity identity,
        string message)
    {
        return Failed(outcome, hash, cacheKey, identity, [CompilerDiagnostic.Create(code, 1, 1, message)]);
    }

    private static CompilerResponse Failed(
        string outcome,
        string hash,
        string cacheKey,
        RevitApiIdentity identity,
        IReadOnlyList<CompilerDiagnostic> diagnostics)
    {
        return new CompilerResponse
        {
            ContractVersion = CompilerProtocol.ContractVersion,
            Outcome = outcome,
            PolicyVersion = CompilerProtocol.PolicyVersion,
            SourceSha256 = hash,
            CacheKey = cacheKey,
            RevitApi = identity,
            Diagnostics = diagnostics,
            Pe = null
        };
    }
}
