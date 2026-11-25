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
            WriteReport(new ProbeReport(false, "compile_failed", level.Code, [], false));
            return "compile_failed";
        }

        _dispatcher ??= RevitExecutionDispatcher.Create();
        var identity = new OpenDocumentIdentityService();
        var service = new CSharpQueryExecutionService(_dispatcher, identity);
        var task = _dispatcher.EnqueueAsync(
            uiApplication =>
            {
                RunQueued(uiApplication, service, identity, level, loop, invalid);
                return 0;
            },
            CancellationToken.None,
            "csharp-query-execution-probe");
        WriteEnqueue(task.IsCompleted);
        return task.IsCompleted ? "completed_before_return" : "scheduled";
    }

    private static void RunQueued(
        UIApplication application,
        CSharpQueryExecutionService service,
        OpenDocumentIdentityService identity,
        CSharpQueryCompilation level,
        CSharpQueryCompilation loop,
        CSharpQueryCompilation invalid)
    {
        var steps = new List<ProbeStep>();
        var contexts = new List<WeakReference>();
        try
        {
            var document = application.ActiveUIDocument?.Document;
            if (document is null || document.IsFamilyDocument || document.IsModified || document.IsModifiable)
            {
                WriteReport(new ProbeReport(false, "document_not_clean", "", steps, false));
                return;
            }

            var documentId = identity.GetId(document);
            steps.Add(Run("normal", service, application, documentId, level, document, contexts));
            steps.Add(Run("repeat", service, application, documentId, level, document, contexts));
            steps.Add(Run("budget", service, application, documentId, loop, document, contexts));
            steps.Add(Run("after_budget", service, application, documentId, level, document, contexts));
            steps.Add(Run("invalid", service, application, documentId, invalid, document, contexts));
            steps.Add(Run("after_invalid", service, application, documentId, level, document, contexts));
            contexts.TrimExcess();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var collected = contexts.Count > 0 && contexts.TrueForAll(context => !context.IsAlive);
            var passed = CollectedPass(steps, collected, document.IsModified);
            WriteReport(new ProbeReport(passed, passed ? "pass" : "fail", "", steps, collected));
        }
        catch (Exception)
        {
            WriteReport(new ProbeReport(false, "probe_failed", "", steps, false));
        }
    }

    private static ProbeStep Run(
        string name,
        CSharpQueryExecutionService service,
        UIApplication application,
        string documentId,
        CSharpQueryCompilation compilation,
        Document document,
        List<WeakReference> contexts)
    {
        var before = document.IsModified;
        var evidence = service.ExecuteNow(application, documentId, compilation);
        if (evidence.LoadContext is not null)
        {
            contexts.Add(evidence.LoadContext);
        }

        var result = evidence.Result;
        return new ProbeStep(
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
            result.ResultJson);
    }

    private static bool CollectedPass(IReadOnlyList<ProbeStep> steps, bool collected, bool modified)
    {
        if (modified || !collected || steps.Count != 6)
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

internal sealed record ProbeReport(
    [property: JsonPropertyName("passed")] bool Passed,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("compile_code")] string CompileCode,
    [property: JsonPropertyName("steps")] IReadOnlyList<ProbeStep> Steps,
    [property: JsonPropertyName("contexts_collected")] bool ContextsCollected);
