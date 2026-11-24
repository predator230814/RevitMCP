using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BimIt.Revit.QueryRuntimeSpike;

internal static class ForbiddenApiAnalyzer
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

    private static readonly HashSet<string> ForbiddenDocumentMembers = new(StringComparer.Ordinal)
    {
        "Save",
        "SaveAs",
        "SynchronizeWithCentral",
        "Export",
        "Print",
        "Close",
        "CloseAndSave",
        "OpenDocumentFile",
        "NewProjectDocument"
    };

    private static readonly HashSet<string> ReflectionMethods = new(StringComparer.Ordinal)
    {
        "GetMethod",
        "GetMethods",
        "GetField",
        "GetFields",
        "GetProperty",
        "GetProperties",
        "GetMember",
        "GetMembers",
        "GetNestedType",
        "GetNestedTypes",
        "InvokeMember",
        "GetCustomAttribute",
        "GetCustomAttributes",
        "CreateInstance",
        "DefineDynamicAssembly",
        "DefineDynamicModule",
        "Load",
        "LoadFrom",
        "LoadFile",
        "LoadModule",
        "DynamicInvoke",
        "GetDelegateForFunctionPointer",
        "PtrToStructure"
    };

    public static IReadOnlyList<QueryDiagnostic> Analyze(CSharpCompilation compilation, SyntaxTree tree)
    {
        var model = compilation.GetSemanticModel(tree);
        var found = new List<QueryDiagnostic>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var root = tree.GetRoot();

        foreach (var token in root.DescendantTokens())
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

        foreach (var node in root.DescendantNodes())
        {
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
                case ExternAliasDirectiveSyntax:
                    Add(found, seen, node.GetLocation(), SyntaxLabel(node) + " is outside the read-only query policy.");
                    break;
                case AttributeSyntax attribute when IsInteropAttribute(attribute.Name.ToString()):
                    Add(found, seen, node.GetLocation(), "P/Invoke is outside the read-only query policy.");
                    break;
                case UsingDirectiveSyntax usingDirective:
                    CheckText(found, seen, usingDirective.NamespaceOrType.ToString(), usingDirective.GetLocation());
                    break;
                case MemberAccessExpressionSyntax or QualifiedNameSyntax or IdentifierNameSyntax or GenericNameSyntax:
                    CheckText(found, seen, node.ToString(), node.GetLocation());
                    break;
            }

            if (model.GetTypeInfo(node).Type is IDynamicTypeSymbol)
            {
                Add(found, seen, node.GetLocation(), "dynamic is outside the read-only query policy.");
            }

            var info = model.GetSymbolInfo(node);
            CheckSymbol(found, seen, info.Symbol, node.GetLocation());
            foreach (var candidate in info.CandidateSymbols)
            {
                CheckSymbol(found, seen, candidate, node.GetLocation());
            }

            CheckSymbol(found, seen, model.GetTypeInfo(node).Type, node.GetLocation());
        }

        return found;
    }

    private static void CheckSymbol(List<QueryDiagnostic> found, HashSet<string> seen, ISymbol? symbol, Location location)
    {
        if (symbol is null || symbol.Kind == SymbolKind.ErrorType || IsHost(symbol))
        {
            return;
        }

        CheckType(found, seen, symbol, location);
        if (symbol is IMethodSymbol method)
        {
            CheckMember(found, seen, method, location);
            CheckType(found, seen, method.ReturnType, location);
            foreach (var parameter in method.Parameters)
            {
                CheckType(found, seen, parameter.Type, location);
            }

            foreach (var argument in method.TypeArguments)
            {
                CheckType(found, seen, argument, location);
            }
        }
        else if (symbol is IPropertySymbol property)
        {
            CheckMember(found, seen, property, location);
            CheckType(found, seen, property.Type, location);
        }
        else if (symbol is INamedTypeSymbol named)
        {
            foreach (var argument in named.TypeArguments)
            {
                CheckType(found, seen, argument, location);
            }
        }
    }

    private static void CheckType(List<QueryDiagnostic> found, HashSet<string> seen, ISymbol symbol, Location location)
    {
        var space = NamespaceOf(symbol);
        if (MatchesPrefix(space, out var prefix))
        {
            Add(found, seen, location, prefix + " is outside the read-only query policy.");
        }

        if (symbol is INamedTypeSymbol named && IsForbiddenRevitType(named))
        {
            Add(found, seen, location, named.Name + " is outside the read-only query policy.");
        }
    }

    private static void CheckMember(List<QueryDiagnostic> found, HashSet<string> seen, ISymbol symbol, Location location)
    {
        var owner = symbol.ContainingType;
        if (owner is null)
        {
            return;
        }

        if (ForbiddenDocumentMembers.Contains(symbol.Name) && IsDocumentOrApplication(owner))
        {
            Add(found, seen, location, symbol.Name + " is outside the read-only query policy.");
        }

        if (symbol is IMethodSymbol && ReflectionMethods.Contains(symbol.Name) && IsReflectionOwner(owner))
        {
            Add(found, seen, location, owner.Name + "." + symbol.Name + " is outside the read-only query policy.");
        }
    }

    private static void CheckText(List<QueryDiagnostic> found, HashSet<string> seen, string? text, Location location)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 300)
        {
            return;
        }

        var normalized = text.Replace("global::", "", StringComparison.Ordinal);
        if (MatchesPrefix(normalized, out var prefix))
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
            if (next is '.' or ' ' or '<' or '(' or '+')
            {
                prefix = candidate;
                return true;
            }
        }

        prefix = "";
        return false;
    }

    private static bool IsHost(ISymbol symbol)
    {
        var space = NamespaceOf(symbol);
        return space == "BimIt.Revit.QueryRuntimeSpike"
            || space.StartsWith("BimIt.Revit.QueryRuntimeSpike.", StringComparison.Ordinal);
    }

    private static bool IsForbiddenRevitType(INamedTypeSymbol type)
    {
        var space = NamespaceOf(type);
        return space.StartsWith("Autodesk.Revit", StringComparison.Ordinal)
            && type.Name is "Transaction" or "SubTransaction" or "TransactionGroup";
    }

    private static bool IsDocumentOrApplication(INamedTypeSymbol type)
    {
        return type.Name is "Document" or "UIDocument" or "Application" or "UIApplication";
    }

    private static bool IsReflectionOwner(INamedTypeSymbol type)
    {
        return type.Name is "Type"
            or "Assembly"
            or "AssemblyBuilder"
            or "Activator"
            or "AppDomain"
            or "MethodInfo"
            or "ConstructorInfo"
            or "FieldInfo"
            or "PropertyInfo"
            or "MethodBase"
            or "Marshal"
            or "RuntimeHelpers";
    }

    private static bool IsInteropAttribute(string name)
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

    private static string SyntaxLabel(SyntaxNode node)
    {
        return node.Kind().ToString();
    }

    private static void Add(List<QueryDiagnostic> found, HashSet<string> seen, Location location, string message)
    {
        var (line, column) = SourcePosition.Of(location);
        var diagnostic = QueryDiagnostic.Error("BIMQ110", line, column, message);
        if (seen.Add(diagnostic.Format()))
        {
            found.Add(diagnostic);
        }
    }
}

internal static class SourcePosition
{
    public static (int Line, int Column) Of(Location location)
    {
        if (!location.IsInSource)
        {
            return (0, 0);
        }

        var span = location.GetMappedLineSpan();
        return (span.StartLinePosition.Line + 1, span.StartLinePosition.Character + 1);
    }
}

internal static class DiagnosticList
{
    public static IReadOnlyList<QueryDiagnostic> Merge(params IEnumerable<QueryDiagnostic>[] groups)
    {
        return groups
            .SelectMany(group => group)
            .OrderBy(diagnostic => diagnostic.Code.StartsWith("BIMQ", StringComparison.Ordinal) ? 0 : 1)
            .ThenBy(diagnostic => diagnostic.Line)
            .ThenBy(diagnostic => diagnostic.Column)
            .ThenBy(diagnostic => diagnostic.Code, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.Message, StringComparer.Ordinal)
            .Take(QueryPolicy.MaxDiagnostics)
            .ToImmutableArray();
    }

    public static IReadOnlyList<QueryDiagnostic> FromCompiler(IEnumerable<Diagnostic> diagnostics)
    {
        var converted = new List<QueryDiagnostic>();
        foreach (var diagnostic in diagnostics)
        {
            if (diagnostic.Severity != DiagnosticSeverity.Error)
            {
                continue;
            }

            var (line, column) = SourcePosition.Of(diagnostic.Location);
            converted.Add(QueryDiagnostic.Error(
                diagnostic.Id,
                line,
                column,
                diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture)));
        }

        return converted;
    }
}
