using System.Reflection;
using System.Text.Json;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BimIt.Revit.Compiler;

namespace BimIt.Revit.CompilerHostProbe;

/// <summary>
/// Opt-in compile-only command. It uses the packaged production CompilerHost and does not load or execute the PE.
/// </summary>
[Transaction(TransactionMode.ReadOnly)]
[Regeneration(RegenerationOption.Manual)]
public sealed class CompilerHostProbeCommand : IExternalCommand
{
    private const string Body = "return new FilteredElementCollector(doc).OfClass(typeof(Level)).GetElementCount();";

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        message = Run();
        return message == "compiled" ? Result.Succeeded : Result.Failed;
    }

    public static string Run()
    {
        var addin = AppDomain.CurrentDomain.GetAssemblies()
            .SingleOrDefault(assembly => assembly.GetName().Name == "BimIt.Revit.Addin");
        if (addin is null || !CompilerHostLocation.TryResolve(addin.Location, out var executable))
        {
            return "compiler_host_not_resolved";
        }

        var loaded = typeof(Document).Assembly;
        var identity = RevitApiIdentity.FromFile(loaded.Location);
        var loadedName = loaded.GetName();
        var same = string.Equals(loadedName.Name, identity.Name, StringComparison.Ordinal)
            && string.Equals(loadedName.Version?.ToString(), identity.Version, StringComparison.Ordinal);
        var request = new CompilerRequest
        {
            ContractVersion = CompilerProtocol.ContractVersion,
            PolicyVersion = CompilerProtocol.PolicyVersion,
            Source = Body,
            RevitApiPath = loaded.Location,
            RevitApi = identity
        };
        var result = CompilerHostClient.Run(executable, request);
        var report = new
        {
            loadedMatchesFile = same,
            apiName = identity.Name,
            apiVersion = identity.Version,
            accepted = result.Accepted,
            code = result.Code,
            outcome = result.Response?.Outcome,
            sourceSha256 = result.Response?.SourceSha256,
            cacheKey = result.Response?.CacheKey,
            peBytes = result.Response?.Pe?.Length ?? 0,
            diagnostics = result.Response?.Diagnostics
        };
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RevitMCP", "spikes");
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, "compiler-host-probe.json"),
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        return result.Accepted && result.Response?.Outcome == CompilerOutcomes.Success && same ? "compiled" : "compile_failed";
    }
}
