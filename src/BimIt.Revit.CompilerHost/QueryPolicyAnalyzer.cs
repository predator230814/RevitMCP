using System.Collections.Immutable;
using BimIt.Revit.Compiler;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BimIt.Revit.CompilerHost;

internal static class QueryPolicyAnalyzer
{
    private static readonly string[] ForbiddenPrefixes =
    [
        "Autodesk.Revit.UI",
        "Autodesk.Revit.ApplicationServices",
        "System.IO",
        "System.Net",
        "System.Diagnostics",
        "System.Reflection",
        "System.Runtime.InteropServices",
        "System.Runtime.Loader",
        "System.Runtime.CompilerServices.Unsafe",
        "System.Threading",
        "System.Linq.Expressions",
        "System.CodeDom",
        "Microsoft.CodeAnalysis",
        "Microsoft.Win32"
    ];

    private static readonly HashSet<string> ForbiddenBclTypes = new(StringComparer.Ordinal)
    {
        "Environment",
        "Console",
        "GC",
        "AppDomain",
        "Activator"
    };

    private static readonly HashSet<string> ForbiddenRevitTypes = new(StringComparer.Ordinal)
    {
        "Transaction",
        "SubTransaction",
        "TransactionGroup",
        "PrintManager",
        "TransmissionData",
        "ExternalResourceUtils",
        "ExternalResourceServerUtils",
        "ExternalResourceServiceUtils",
        "IExternalResourceServer",
        "WorksharingUtils"
    };

    private static readonly HashSet<string> ForbiddenEffectMembers = new(StringComparer.Ordinal)
    {
        "Save",
        "SaveAs",
        "SaveAsCloudModel",
        "SaveCloudModel",
        "SaveToProjectAsImage",
        "Close",
        "Export",
        "ExportImage",
        "Import",
        "Link",
        "Print",
        "SynchronizeWithCentral",
        "ReloadLatest",
        "EnableWorksharing",
        "EnableCloudWorksharing",
        "LoadFamily",
        "LoadFamilySymbol",
        "EditFamily",
        "OpenDocumentFile",
        "OpenIFCDocument",
        "OpenSharedParameterFile",
        "ExtractPartAtomFromFamilyFile",
        "Regenerate",
        "PostFailure",
        "PurgeReleasedAPIObjects"
    };

    public static IReadOnlyList<CompilerDiagnostic> Analyze(CSharpCompilation compilation, SyntaxTree tree)
    {
        var model = compilation.GetSemanticModel(tree);
        var found = new List<CompilerDiagnostic>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in tree.GetRoot().DescendantNodesAndTokens())
        {
            if (node.IsToken && IsUser(node.AsToken().GetLocation()))
            {
                RejectToken(found, seen, node.AsToken());
            }

            if (node.IsNode)
            {
                RejectNode(found, seen, model, node.AsNode()!);
            }
        }

        return found;
    }

    private static void RejectToken(List<CompilerDiagnostic> found, HashSet<string> seen, SyntaxToken token)
    {
        if (token.IsKind(SyntaxKind.UnsafeKeyword)
            || token.IsKind(SyntaxKind.AsyncKeyword)
            || token.IsKind(SyntaxKind.AwaitKeyword)
            || token.IsKind(SyntaxKind.GotoKeyword)
            || token.IsKind(SyntaxKind.ExternKeyword))
        {
            Add(found, seen, token.GetLocation(), token.Text + " is outside the read-only query policy.");
        }
    }

    private static void RejectNode(List<CompilerDiagnostic> found, HashSet<string> seen, SemanticModel model, SyntaxNode node)
    {
        if (!IsUser(node.GetLocation()) && node is not AnonymousFunctionExpressionSyntax)
        {
            return;
        }

        switch (node)
        {
            case UnsafeStatementSyntax:
            case PointerTypeSyntax:
            case StackAllocArrayCreationExpressionSyntax:
            case FixedStatementSyntax:
            case TryStatementSyntax:
            case CatchClauseSyntax:
            case LockStatementSyntax:
            case GotoStatementSyntax:
            case LabeledStatementSyntax:
            case YieldStatementSyntax:
            case LocalFunctionStatementSyntax:
            case UsingDirectiveSyntax:
                Add(found, seen, node.GetLocation(), SyntaxName(node) + " is outside the read-only query policy.");
                return;
            case AttributeSyntax attribute when IsInterop(attribute.Name.ToString()):
                Add(found, seen, node.GetLocation(), "P/Invoke is outside the read-only query policy.");
                return;
            case AnonymousFunctionExpressionSyntax lambda when ReferencesOwnLocal(model, lambda):
                Add(found, seen, node.GetLocation(), "self-referential delegate is outside the read-only query policy.");
                break;
            case MemberAccessExpressionSyntax or QualifiedNameSyntax or IdentifierNameSyntax:
                CheckText(found, seen, node.ToString(), node.GetLocation());
                break;
        }

        if (model.GetTypeInfo(node).Type is IDynamicTypeSymbol)
        {
            Add(found, seen, node.GetLocation(), "dynamic is outside the read-only query policy.");
        }

        var info = model.GetSymbolInfo(node);
        CheckSymbol(found, seen, info.Symbol, node);
        foreach (var candidate in info.CandidateSymbols)
        {
            CheckSymbol(found, seen, candidate, node);
        }

        CheckSymbol(found, seen, model.GetTypeInfo(node).Type, node);
        if (node is InvocationExpressionSyntax invocation
            && invocation.Expression is MemberAccessExpressionSyntax access
            && ForbiddenEffectMembers.Contains(access.Name.Identifier.ValueText))
        {
            Add(found, seen, access.Name.GetLocation(), access.Name.Identifier.ValueText + " is outside the read-only query policy.");
        }
    }

    private static bool ReferencesOwnLocal(SemanticModel model, AnonymousFunctionExpressionSyntax lambda)
    {
        var local = BoundLocal(model, lambda);
        if (local is null)
        {
            return false;
        }

        foreach (var name in lambda.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            var symbol = model.GetSymbolInfo(name).Symbol;
            if (SymbolEqualityComparer.Default.Equals(symbol, local))
            {
                return true;
            }
        }

        return false;
    }

    private static ILocalSymbol? BoundLocal(SemanticModel model, AnonymousFunctionExpressionSyntax lambda)
    {
        if (lambda.Parent is EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax declarator })
        {
            return model.GetDeclaredSymbol(declarator) as ILocalSymbol;
        }

        if (lambda.Parent is AssignmentExpressionSyntax { Left: IdentifierNameSyntax left })
        {
            return model.GetSymbolInfo(left).Symbol as ILocalSymbol;
        }

        return null;
    }

    private static void CheckSymbol(List<CompilerDiagnostic> found, HashSet<string> seen, ISymbol? symbol, SyntaxNode node)
    {
        if (symbol is null || symbol.Kind == SymbolKind.ErrorType || IsGenerated(symbol))
        {
            return;
        }

        CheckType(found, seen, symbol, node.GetLocation());
        if (symbol is IMethodSymbol method)
        {
            CheckMember(found, seen, method, node.GetLocation());
            CheckType(found, seen, method.ReturnType, node.GetLocation());
            foreach (var parameter in method.Parameters)
            {
                CheckType(found, seen, parameter.Type, node.GetLocation());
            }
        }
        else if (symbol is IPropertySymbol property)
        {
            CheckMember(found, seen, property, node.GetLocation());
            CheckType(found, seen, property.Type, node.GetLocation());
        }
        else if (symbol is IEventSymbol)
        {
            Add(found, seen, node.GetLocation(), "event subscription is outside the read-only query policy.");
        }
    }

    private static void CheckType(List<CompilerDiagnostic> found, HashSet<string> seen, ISymbol symbol, Location location)
    {
        var space = NamespaceOf(symbol);
        if (MatchesPrefix(space, out var prefix))
        {
            Add(found, seen, location, prefix + " is outside the read-only query policy.");
        }

        if (symbol is not INamedTypeSymbol named)
        {
            return;
        }

        if (named.ContainingNamespace?.ToDisplayString() == "System" && ForbiddenBclTypes.Contains(named.Name))
        {
            Add(found, seen, location, named.Name + " is outside the read-only query policy.");
        }

        if (space.StartsWith("Autodesk.Revit", StringComparison.Ordinal) && ForbiddenRevitTypes.Contains(named.Name))
        {
            Add(found, seen, location, named.Name + " is outside the read-only query policy.");
        }

        if (named.Name == "Type" && named.ContainingNamespace?.ToDisplayString() == "System" && symbol.Kind != SymbolKind.NamedType)
        {
            Add(found, seen, location, "System.Type reflection is outside the read-only query policy.");
        }
    }

    private static void CheckMember(List<CompilerDiagnostic> found, HashSet<string> seen, ISymbol symbol, Location location)
    {
        var owner = symbol.ContainingType;
        if (owner is null)
        {
            return;
        }

        if (symbol.Name == "GetType")
        {
            Add(found, seen, location, "System.Type reflection is outside the read-only query policy.");
        }

        if (owner.ContainingNamespace?.ToDisplayString() == "System" && ForbiddenBclTypes.Contains(owner.Name))
        {
            Add(found, seen, location, owner.Name + " is outside the read-only query policy.");
        }

        var ownerSpace = NamespaceOf(owner);
        if (owner.Name == "Type" && ownerSpace == "System")
        {
            Add(found, seen, location, "System.Type reflection is outside the read-only query policy.");
        }

        if (ForbiddenEffectMembers.Contains(symbol.Name)
            && owner.Name is "Document" or "UIDocument" or "Application" or "UIApplication")
        {
            Add(found, seen, location, symbol.Name + " is outside the read-only query policy.");
        }

        if (ownerSpace.StartsWith("Autodesk.Revit", StringComparison.Ordinal)
            && symbol is IMethodSymbol
            && (symbol.Name.StartsWith("add_", StringComparison.Ordinal) || symbol.Name.StartsWith("remove_", StringComparison.Ordinal)))
        {
            Add(found, seen, location, "event subscription is outside the read-only query policy.");
        }
    }

    private static bool IsGenerated(ISymbol symbol)
    {
        return symbol.Name == CompilerProtocol.ChargeIdentifier
            || symbol.ContainingType?.Name == CompilerProtocol.GeneratedTypeName;
    }

    private static bool IsUser(Location location)
    {
        if (!location.IsInSource)
        {
            return false;
        }

        var mapped = location.GetMappedLineSpan();
        return string.Equals(mapped.Path, CompilerProtocol.QueryPath, StringComparison.Ordinal);
    }

    private static void CheckText(List<CompilerDiagnostic> found, HashSet<string> seen, string text, Location location)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 300)
        {
            return;
        }

        if (MatchesPrefix(text.Replace("global::", "", StringComparison.Ordinal), out var prefix))
        {
            Add(found, seen, location, prefix + " is outside the read-only query policy.");
        }
    }

    private static bool MatchesPrefix(string text, out string prefix)
    {
        foreach (var candidate in ForbiddenPrefixes)
        {
            if (!text.StartsWith(candidate, StringComparison.Ordinal))
            {
                continue;
            }

            if (text.Length == candidate.Length)
            {
                prefix = candidate;
                return true;
            }

            var next = text[candidate.Length];
            if (next is '.' or '+' or '<')
            {
                prefix = candidate;
                return true;
            }
        }

        prefix = "";
        return false;
    }

    private static bool IsInterop(string name)
    {
        return name.Contains("DllImport", StringComparison.Ordinal)
            || name.Contains("LibraryImport", StringComparison.Ordinal)
            || name.Contains("UnmanagedCallersOnly", StringComparison.Ordinal);
    }

    private static string NamespaceOf(ISymbol symbol)
    {
        if (symbol is INamespaceSymbol space)
        {
            return space.ToDisplayString();
        }

        return symbol.ContainingNamespace?.ToDisplayString() ?? "";
    }

    private static string SyntaxName(SyntaxNode node)
    {
        return node.Kind().ToString();
    }

    private static void Add(List<CompilerDiagnostic> found, HashSet<string> seen, Location location, string message)
    {
        if (!IsUser(location) && !location.IsInSource)
        {
            return;
        }

        var span = location.GetMappedLineSpan();
        var diagnostic = CompilerDiagnostic.Create(
            CompilerResultCodes.PolicyRejected,
            span.StartLinePosition.Line + 1,
            span.StartLinePosition.Character + 1,
            message);
        if (seen.Add(diagnostic.Code + ":" + diagnostic.Line + ":" + diagnostic.Column + ":" + diagnostic.Message))
        {
            found.Add(diagnostic);
        }
    }
}

internal static class CompilerDiagnosticList
{
    public static IReadOnlyList<CompilerDiagnostic> Merge(params IEnumerable<CompilerDiagnostic>[] groups)
    {
        return groups
            .SelectMany(group => group)
            .Take(CompilerProtocol.MaxDiagnostics)
            .ToImmutableArray();
    }

    public static IReadOnlyList<CompilerDiagnostic> FromCompiler(IEnumerable<Diagnostic> diagnostics)
    {
        var converted = new List<CompilerDiagnostic>();
        foreach (var diagnostic in diagnostics)
        {
            if (diagnostic.Severity != DiagnosticSeverity.Error)
            {
                continue;
            }

            var span = diagnostic.Location.GetMappedLineSpan();
            converted.Add(CompilerDiagnostic.Create(
                diagnostic.Id,
                diagnostic.Location.IsInSource ? span.StartLinePosition.Line + 1 : 0,
                diagnostic.Location.IsInSource ? span.StartLinePosition.Character + 1 : 0,
                diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture)));
        }

        return converted;
    }
}
