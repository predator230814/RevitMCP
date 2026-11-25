using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BimIt.Revit.Addin.CSharpQuery;
using BimIt.Revit.Addin.Execution;
using BimIt.Revit.Addin.Identity;

namespace BimIt.Revit.CSharpQueryExecutionProbe;

/// <summary>
/// Opt-in command. It compiles with the packaged CompilerHost, then runs the production execution kernel
/// only inside a queued <see cref="RevitExecutionDispatcher"/> item. It does not register an MCP tool.
/// </summary>
[Transaction(TransactionMode.ReadOnly)]
[Regeneration(RegenerationOption.Manual)]
public sealed class CSharpQueryExecutionProbeCommand : IExternalCommand
{
    private const string LevelProjection =
        "return new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().Select(level => new { element_ref = elementRef(level), name = level.Name, elevation = level.Elevation }).ToList();";

    private const string TightLoop =
        "var total = 0; for (var i = 0; i < 20000; i++) { total++; } return total;";

    private const string InvalidResult =
        "return new FilteredElementCollector(doc).OfClass(typeof(Level));";

    private static RevitExecutionDispatcher? _dispatcher;

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        message = Schedule(commandData.Application);
        return message == "scheduled" ? Result.Succeeded : Result.Failed;
    }

    public static string Schedule(UIApplication application)
    {
        ArgumentNullException.ThrowIfNull(application);
        var compiler = new CSharpQueryCompilationService();
        var level = compiler.Compile(LevelProjection);
        var loop = compiler.Compile(TightLoop);
        var invalid = compiler.Compile(InvalidResult);
        if (!level.Success || !loop.Success || !invalid.Success)
        {
            WriteReport(new ProbeReport(false, "compile_failed", level.Code, [], [], false, 0));
            return "compile_failed";
        }

        _dispatcher ??= RevitExecutionDispatcher.Create();
        var identity = new OpenDocumentIdentityService();
        var service = new CSharpQueryExecutionService(_dispatcher, identity);
        var task = _dispatcher.EnqueueAsync(
            uiApplication => ExecuteRuns(uiApplication, service, identity, level, loop, invalid),
            CancellationToken.None,
            "csharp-query-execution-probe");
        ObserveAfterReturn(task);
        WriteEnqueue(task.IsCompleted);
        return task.IsCompleted ? "completed_before_return" : "scheduled";
    }

    private static void ObserveAfterReturn(Task<ProbePayload> task)
    {
        task.ContinueWith(
            completed =>
            {
                if (completed.IsFaulted)
                {
                    WriteReport(new ProbeReport(false, "probe_failed", "", [], [], false, 0));
                    return;
                }

                Publish(completed.Result);
            },
            CancellationToken.None,
            TaskContinuationOptions.None,
            TaskScheduler.Default);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ProbePayload ExecuteRuns(
        UIApplication application,
        CSharpQueryExecutionService service,
        OpenDocumentIdentityService identity,
        CSharpQueryCompilation level,
        CSharpQueryCompilation loop,
        CSharpQueryCompilation invalid)
    {
        var payload = new ProbePayload();
        try
        {
            var document = application.ActiveUIDocument?.Document;
            if (document is null || document.IsFamilyDocument || document.IsModified || document.IsModifiable)
            {
                payload.Status = "document_not_clean";
                return payload;
            }

            var documentId = identity.GetId(document);
            payload.Modified = document.IsModified;
            AddRun(payload, "normal", service, application, documentId, level, document);
            AddRun(payload, "repeat", service, application, documentId, level, document);
            AddRun(payload, "budget", service, application, documentId, loop, document);
            AddRun(payload, "after_budget", service, application, documentId, level, document);
            AddRun(payload, "invalid", service, application, documentId, invalid, document);
            AddRun(payload, "after_invalid", service, application, documentId, level, document);
            payload.Modified = document.IsModified;
            return payload;
        }
        catch (Exception)
        {
            payload.Status = "probe_failed";
            return payload;
        }
    }

    private static void Publish(ProbePayload payload)
    {
        if (payload.Status != "ready")
        {
            WriteReport(new ProbeReport(false, payload.Status, payload.CompileCode, payload.Steps, [], false, 0));
            return;
        }

        var cycles = Collect(payload.Contexts);
        var observations = new List<ProbeContextObservation>(payload.Contexts.Count);
        foreach (var context in payload.Contexts)
        {
            observations.Add(new ProbeContextObservation(context.Name, StateOf(context.Reference)));
        }

        var collected = observations.Count == 6 && observations.TrueForAll(item => item.State == "collected");
        var passed = collected && CollectedPass(payload.Steps, payload.Modified);
        WriteReport(new ProbeReport(
            passed,
            passed ? "pass" : "fail",
            "",
            payload.Steps,
            observations,
            collected,
            cycles));
    }

    private static int Collect(List<ProbeContext> contexts)
    {
        for (var cycle = 1; cycle <= 10; cycle++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            if (contexts.TrueForAll(context => context.Reference is not null && !context.Reference.IsAlive))
            {
                return cycle;
            }
        }

        return 10;
    }

    private static string StateOf(WeakReference? reference)
    {
        if (reference is null)
        {
            return "missing";
        }

        return reference.IsAlive ? "alive" : "collected";
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AddRun(
        ProbePayload payload,
        string name,
        CSharpQueryExecutionService service,
        UIApplication application,
        string documentId,
        CSharpQueryCompilation compilation,
        Document document)
    {
        var before = document.IsModified;
        var evidence = service.ExecuteNow(application, documentId, compilation);
        payload.Contexts.Add(new ProbeContext(name, evidence.LoadContext));
        var result = evidence.Result;
        payload.Steps.Add(new ProbeStep(
            name,
            result.Success,
            result.Code,
            result.Reason,
            result.SourceSha256,
            result.CacheKey,
            result.ApiName,
            result.ApiVersion,
            result.CompileOutcome,
            compilation.Pe?.Length ?? 0,
            result.ResultCharacters,
            result.DurationMilliseconds,
            before,
            document.IsModified,
            result.ResultJson));
    }

    private static bool CollectedPass(IReadOnlyList<ProbeStep> steps, bool modified)
    {
        if (modified || steps.Count != 6)
        {
            return false;
        }

        return steps[0].Success
            && steps[1].Success
            && steps[0].SourceSha256 == steps[1].SourceSha256
            && steps[0].CacheKey == steps[1].CacheKey
            && steps[0].ApiVersion == "26.5.0.0"
            && !steps[0].ModifiedBefore
            && !steps[0].ModifiedAfter
            && !steps[1].ModifiedAfter
            && steps[2].Code == CSharpQueryCodes.BudgetExceeded
            && steps[2].ResultJson is null
            && steps[3].Success
            && steps[4].Code == CSharpQueryCodes.InvalidResult
            && steps[4].ResultJson is null
            && steps[5].Success;
    }

    private static void WriteEnqueue(bool completedBeforeReturn)
    {
        File.WriteAllText(
            Path.Combine(ReportDirectory(), "csharp-query-execution-probe.enqueue.json"),
            JsonSerializer.Serialize(new EnqueueObservation(completedBeforeReturn), JsonOptions()));
    }

    private static void WriteReport(ProbeReport report)
    {
        File.WriteAllText(
            Path.Combine(ReportDirectory(), "csharp-query-execution-probe.json"),
            JsonSerializer.Serialize(report, JsonOptions()));
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

    private static JsonSerializerOptions JsonOptions()
    {
        return new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = true
        };
    }
}

internal sealed record EnqueueObservation(
    [property: JsonPropertyName("completed_before_return")] bool CompletedBeforeReturn);

internal sealed record ProbeStep(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("reason")] string? Reason,
    [property: JsonPropertyName("source_sha256")] string SourceSha256,
    [property: JsonPropertyName("cache_key")] string CacheKey,
    [property: JsonPropertyName("api_name")] string? ApiName,
    [property: JsonPropertyName("api_version")] string? ApiVersion,
    [property: JsonPropertyName("compile_outcome")] string CompileOutcome,
    [property: JsonPropertyName("pe_bytes")] int PeBytes,
    [property: JsonPropertyName("result_characters")] int ResultCharacters,
    [property: JsonPropertyName("duration_ms")] int DurationMilliseconds,
    [property: JsonPropertyName("modified_before")] bool ModifiedBefore,
    [property: JsonPropertyName("modified_after")] bool ModifiedAfter,
    [property: JsonPropertyName("result_json")] string? ResultJson);

internal sealed class ProbePayload
{
    public string Status { get; set; } = "ready";

    public string CompileCode { get; set; } = "";

    public List<ProbeStep> Steps { get; } = [];

    public List<ProbeContext> Contexts { get; } = [];

    public bool Modified { get; set; }
}

internal sealed record ProbeContext(string Name, WeakReference? Reference);

internal sealed record ProbeContextObservation(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("state")] string State);

internal sealed record ProbeReport(
    [property: JsonPropertyName("passed")] bool Passed,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("compile_code")] string CompileCode,
    [property: JsonPropertyName("steps")] IReadOnlyList<ProbeStep> Steps,
    [property: JsonPropertyName("contexts")] IReadOnlyList<ProbeContextObservation> Contexts,
    [property: JsonPropertyName("contexts_collected")] bool ContextsCollected,
    [property: JsonPropertyName("gc_cycles")] int GcCycles);
