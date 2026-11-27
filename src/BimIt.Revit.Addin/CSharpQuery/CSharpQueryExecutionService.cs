using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BimIt.Revit.Addin.Execution;
using BimIt.Revit.Addin.Identity;
using BimIt.Revit.Compiler;

namespace BimIt.Revit.Addin.CSharpQuery;

internal sealed class CSharpQueryExecutionService : ICSharpQueryExecutor
{
    private readonly RevitExecutionDispatcher _dispatcher;
    private readonly OpenDocumentIdentityService _identity;

    public CSharpQueryExecutionService(RevitExecutionDispatcher dispatcher, OpenDocumentIdentityService identity)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(identity);
        _dispatcher = dispatcher;
        _identity = identity;
    }

    public Task<CSharpQueryExecution> ExecuteAsync(
        string documentId,
        CSharpQueryCompilation compilation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        if (!compilation.Success || compilation.Pe is null)
        {
            return Task.FromResult(RejectedCompilation(compilation));
        }

        return _dispatcher.EnqueueAsync(
            application => ExecuteNow(application, documentId, compilation).Result,
            cancellationToken);
    }

    internal CSharpQueryExecutionEvidence ExecuteNow(
        UIApplication application,
        string documentId,
        CSharpQueryCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(compilation);
        if (!compilation.Success || compilation.Pe is null)
        {
            return new CSharpQueryExecutionEvidence(RejectedCompilation(compilation), null);
        }

        var decision = CSharpQueryDocumentGate.Decide(Observe(application, documentId, compilation));
        if (!decision.Accepted)
        {
            return new CSharpQueryExecutionEvidence(
                CSharpQueryExecution.Failed(decision.Code!, decision.Reason, 0).WithCompilation(compilation),
                null);
        }

        var evidence = CSharpQueryExecutionKernel.Execute(
            compilation.Pe,
            application.ActiveUIDocument!.Document,
            CSharpQueryElementRef.Reference);
        return evidence with { Result = evidence.Result.WithCompilation(compilation) };
    }

    private CSharpQueryDocumentObservation Observe(
        UIApplication application,
        string documentId,
        CSharpQueryCompilation compilation)
    {
        var uiDocument = application.ActiveUIDocument;
        var document = uiDocument?.Document;
        if (uiDocument is null || document is null)
        {
            return CSharpQueryDocumentObservation.None;
        }

        var family = document.IsFamilyDocument;
        var matches = false;
        if (!family)
        {
            try
            {
                matches = string.Equals(_identity.GetId(document), documentId, StringComparison.Ordinal);
            }
            catch (Exception)
            {
                matches = false;
            }
        }

        return new CSharpQueryDocumentObservation(
            true,
            family,
            matches,
            document.IsModifiable,
            ApiMatches(compilation));
    }

    private static bool ApiMatches(CSharpQueryCompilation compilation)
    {
        if (compilation.Api is null || !compilation.Api.IsSupportedApiIdentity)
        {
            return false;
        }

        try
        {
            var loaded = typeof(Document).Assembly;
            var location = loaded.Location;
            if (string.IsNullOrWhiteSpace(location))
            {
                return false;
            }

            return RevitApiIdentity.FromFile(location).Matches(compilation.Api);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static CSharpQueryExecution RejectedCompilation(CSharpQueryCompilation compilation)
    {
        var code = string.IsNullOrWhiteSpace(compilation.Code)
            ? CSharpQueryCodes.ExecutionFailed
            : compilation.Code;
        return CSharpQueryExecution.Failed(code, null, 0).WithCompilation(compilation);
    }
}
