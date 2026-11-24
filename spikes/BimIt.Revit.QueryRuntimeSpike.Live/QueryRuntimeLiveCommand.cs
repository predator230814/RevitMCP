using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BimIt.Revit.QueryRuntimeSpike;

namespace BimIt.Revit.QueryRuntimeSpike.Live;

/// <summary>
/// Opt-in command. It runs on the Revit API thread and does not enter the production EXEC-0001 dispatcher.
/// </summary>
[Transaction(TransactionMode.ReadOnly)]
[Regeneration(RegenerationOption.Manual)]
public sealed class QueryRuntimeLiveCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var document = commandData.Application.ActiveUIDocument?.Document;
        if (document is null)
        {
            message = "no_active_document";
            return Result.Cancelled;
        }

        var readiness = DocumentReadiness.From(document);
        var rejection = readiness.RejectionCode();
        if (rejection is not null)
        {
            message = rejection;
            TaskDialog.Show("Query runtime spike", "Document was not executed: " + rejection);
            return Result.Cancelled;
        }

        var before = document.IsModified;
        var compiler = QueryCompiler.ForRevitApi(typeof(Document).Assembly.Location);
        var compiled = compiler.Compile(QueryRuntimeSamples.LevelProjection);
        if (!compiled.Success)
        {
            message = string.Join(" | ", compiled.Diagnostics.Select(diagnostic => diagnostic.Format()));
            TaskDialog.Show("Query runtime spike", message);
            return Result.Failed;
        }

        var evidence = QueryExecutor.Execute(
            compiled,
            readiness,
            document,
            new Func<Element, string>(ElementRef),
            new QueryBudget(QueryPolicy.DefaultBudget));
        var after = document.IsModified;
        var unchanged = before == after;
        var diagnosticText = string.Join(" | ", evidence.Result.Diagnostics.Select(diagnostic => diagnostic.Format()));
        message = "unchanged=" + unchanged + "; IsModified " + before + " -> " + after + "; " + (evidence.Result.Json ?? diagnosticText);
        TaskDialog.Show(
            "Query runtime spike",
            "API thread. Production EXEC-0001 was not entered.\n"
            + "Document unchanged: " + unchanged + "\n"
            + "IsModified before=" + before + " after=" + after + "\n"
            + (evidence.Result.Json ?? diagnosticText));
        return evidence.Result.Success && unchanged ? Result.Succeeded : Result.Failed;
    }

    private static string ElementRef(Element element)
    {
        return element is null ? "" : element.UniqueId;
    }
}
