namespace BimIt.Revit.Addin.CSharpQuery;

internal interface ICSharpQueryCompiler
{
    CSharpQueryCompilation Compile(string source);
}

internal interface ICSharpQueryExecutor
{
    Task<CSharpQueryExecution> ExecuteAsync(
        string documentId,
        CSharpQueryCompilation compilation,
        CancellationToken cancellationToken);
}
