using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BimIt.Revit.Addin.Execution;
using BimIt.Revit.QueryRuntimeSpike;

namespace BimIt.Revit.QueryRuntimeSpike.Live;

/// <summary>
/// Opt-in command. It creates a spike-owned production dispatcher and returns without waiting for that dispatcher.
/// The generated query runs only inside the queued work item. This is not an MCP tool.
/// </summary>
[Transaction(TransactionMode.ReadOnly)]
[Regeneration(RegenerationOption.Manual)]
public sealed class QueryRuntimeLiveCommand : IExternalCommand
{
    private static RevitExecutionDispatcher? _dispatcher;

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        message = Schedule(commandData.Application);
        return message == "scheduled" ? Result.Succeeded : Result.Failed;
    }

    public static string Schedule(UIApplication application)
    {
        ArgumentNullException.ThrowIfNull(application);
        var liveLocation = typeof(QueryRuntimeLiveCommand).Assembly.Location;
        var addin = LoadedAssemblySelection.SingleNamed(AppDomain.CurrentDomain.GetAssemblies(), ProductionAddinBinding.AddinName);
        if (addin is null || LoadedAssemblySelection.SharesDirectory(addin.Location, liveLocation))
        {
            return "production_addin_not_resolved";
        }

        var apiPath = typeof(Document).Assembly.Location;
        var compiler = QueryCompiler.ForRevitApi(apiPath);
        var gate = QueryExecGate.Prepare(compiler);
        if (!gate.LevelQuery.Success || !gate.TightLoop.Success)
        {
            return "query_compile_failed";
        }

        if (gate.TransactionAttempt.Success || gate.TransactionAttempt.Assembly is not null || gate.TransactionExecutionEnqueued)
        {
            return "transaction_query_was_executable";
        }

        _dispatcher ??= RevitExecutionDispatcher.Create();
        var runId = Guid.NewGuid().ToString("N");
        var preamble = new GatePreamble(
            runId,
            application.Application.VersionNumber,
            application.Application.VersionBuild,
            typeof(Document).Assembly.GetName().Name ?? "",
            typeof(Document).Assembly.GetName().Version?.ToString() ?? "",
            compiler.ApiIdentity.Name,
            compiler.ApiIdentity.Version,
            gate.LevelQuery.CacheKey,
            gate.TransactionAttempt.Diagnostics.FirstOrDefault(diagnostic => diagnostic.Code == "BIMQ110")?.Code,
            addin.GetName().Version?.ToString() ?? "");
        var task = _dispatcher.EnqueueAsync(
            uiApplication =>
            {
                RunQueued(uiApplication, gate, preamble);
                return 0;
            },
            CancellationToken.None,
            "query-runtime-spike");
        WriteObservation(runId, task.IsCompleted);
        return "scheduled";
    }

    private static void RunQueued(UIApplication application, QueryExecGate gate, GatePreamble preamble)
    {
        try
        {
            var observation = ReadObservation(preamble.RunId);
            var document = application.ActiveUIDocument?.Document;
            IReadOnlyList<QueryExecStepResult> steps = document is null
                ? []
                : QueryExecSequence.Run(
                    gate,
                    (compilation, budget) => QueryExecutor.Execute(
                        compilation,
                        DocumentReadiness.From(document),
                        document,
                        new Func<Element, string>(ElementRef),
                        new QueryBudget(budget)),
                    () => document.IsModified);
            var report = new GateReport(
                preamble,
                observation,
                document is not null,
                steps);
            WriteReport(report);
        }
        catch (Exception ex)
        {
            var failed = new GateReport(preamble, null, false, [], ex.GetType().Name);
            WriteReport(failed);
        }
    }

    private static string ElementRef(Element element)
    {
        return element is null ? "" : element.UniqueId;
    }

    private static string ReportDirectory()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RevitMCP",
            "spikes");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string ObservationPath(string runId)
    {
        return Path.Combine(ReportDirectory(), "query-runtime-exec-" + runId + ".enqueue.json");
    }

    private static void WriteObservation(string runId, bool completedBeforeReturn)
    {
        File.WriteAllText(
            ObservationPath(runId),
            JsonSerializer.Serialize(new EnqueueObservation(completedBeforeReturn), GateJson.Options));
    }

    private static EnqueueObservation? ReadObservation(string runId)
    {
        var path = ObservationPath(runId);
        if (!File.Exists(path))
        {
            return null;
        }

        return JsonSerializer.Deserialize<EnqueueObservation>(File.ReadAllText(path), GateJson.Options);
    }

    private static void WriteReport(GateReport report)
    {
        File.WriteAllText(
            Path.Combine(ReportDirectory(), "query-runtime-exec-gate.json"),
            JsonSerializer.Serialize(report, GateJson.Options));
    }
}

internal static class GateJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };
}

internal sealed record EnqueueObservation(
    [property: JsonPropertyName("completed_before_return")] bool CompletedBeforeReturn);

internal sealed record GatePreamble(
    [property: JsonPropertyName("run_id")] string RunId,
    [property: JsonPropertyName("revit_version")] string RevitVersion,
    [property: JsonPropertyName("revit_build")] string RevitBuild,
    [property: JsonPropertyName("loaded_revit_api_name")] string LoadedRevitApiName,
    [property: JsonPropertyName("loaded_revit_api_version")] string LoadedRevitApiVersion,
    [property: JsonPropertyName("compiler_revit_api_name")] string CompilerRevitApiName,
    [property: JsonPropertyName("compiler_revit_api_version")] string CompilerRevitApiVersion,
    [property: JsonPropertyName("cache_key")] string CacheKey,
    [property: JsonPropertyName("transaction_code")] string? TransactionCode,
    [property: JsonPropertyName("production_addin_version")] string ProductionAddinVersion);

internal sealed record GateReport(
    GatePreamble Preamble,
    EnqueueObservation? Observation,
    bool HasDocument,
    IReadOnlyList<QueryExecStepResult> Steps,
    string? Failure = null)
{
    public string Summary()
    {
        var passed = Failure is null
            && Observation is { CompletedBeforeReturn: false }
            && Preamble.TransactionCode == "BIMQ110"
            && Preamble.LoadedRevitApiName == "RevitAPI"
            && Preamble.LoadedRevitApiName == Preamble.CompilerRevitApiName
            && Preamble.LoadedRevitApiVersion == Preamble.CompilerRevitApiVersion
            && HasDocument
            && Steps.Count == 4
            && Steps[0].Success
            && Steps[1].Success
            && Steps[0].Json == Steps[1].Json
            && Steps[0].CacheKey == Steps[1].CacheKey
            && Steps[0].ContextCollected
            && Steps[1].ContextCollected
            && !Steps[0].ModifiedBefore
            && !Steps[0].ModifiedAfter
            && !Steps[1].ModifiedBefore
            && !Steps[1].ModifiedAfter
            && !Steps[2].Success
            && Steps[2].Code == "BIMQ120"
            && Steps[2].ContextCollected
            && Steps[3].Success
            && Steps[3].ContextCollected
            && !Steps[3].ModifiedBefore
            && !Steps[3].ModifiedAfter
            && Steps[3].CacheKey == Steps[0].CacheKey;
        return passed
            ? "PASS. Bounded read ran through the production dispatcher. This is not an OS sandbox."
            : "FAIL. See %LOCALAPPDATA%\\RevitMCP\\spikes\\query-runtime-exec-gate.json.";
    }
}
