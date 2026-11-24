using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BimIt.Revit.QueryRuntimeSpike;

internal static class LoopBudgetRewriter
{
    public static SyntaxTree Rewrite(SyntaxTree tree)
    {
        var root = (CompilationUnitSyntax)new Rewriter().Visit(tree.GetRoot())!;
        return CSharpSyntaxTree.Create(root, (CSharpParseOptions)tree.Options, tree.FilePath, tree.Encoding);
    }

    private sealed class Rewriter : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitForStatement(ForStatementSyntax node)
        {
            var visited = (ForStatementSyntax)base.VisitForStatement(node)!;
            return visited.WithStatement(Instrument(visited.Statement));
        }

        public override SyntaxNode? VisitForEachStatement(ForEachStatementSyntax node)
        {
            var visited = (ForEachStatementSyntax)base.VisitForEachStatement(node)!;
            return visited.WithStatement(Instrument(visited.Statement));
        }

        public override SyntaxNode? VisitForEachVariableStatement(ForEachVariableStatementSyntax node)
        {
            var visited = (ForEachVariableStatementSyntax)base.VisitForEachVariableStatement(node)!;
            return visited.WithStatement(Instrument(visited.Statement));
        }

        public override SyntaxNode? VisitWhileStatement(WhileStatementSyntax node)
        {
            var visited = (WhileStatementSyntax)base.VisitWhileStatement(node)!;
            return visited.WithStatement(Instrument(visited.Statement));
        }

        public override SyntaxNode? VisitDoStatement(DoStatementSyntax node)
        {
            var visited = (DoStatementSyntax)base.VisitDoStatement(node)!;
            return visited.WithStatement(Instrument(visited.Statement));
        }

        private static StatementSyntax Instrument(StatementSyntax statement)
        {
            var charge = SyntaxFactory.ParseStatement("__budget.Charge();\r\n");
            if (statement is BlockSyntax block)
            {
                return block.WithStatements(block.Statements.Insert(0, charge));
            }

            return SyntaxFactory.Block(charge, statement);
        }
    }
}
