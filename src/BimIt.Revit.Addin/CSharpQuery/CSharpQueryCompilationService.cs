using BimIt.Revit.Compiler;

namespace BimIt.Revit.Addin.CSharpQuery;

internal sealed class CSharpQueryCompilation
{
    private CSharpQueryCompilation(
        bool success,
        string code,
        byte[]? pe,
        string sourceSha256,
        string cacheKey,
        RevitApiIdentity? api,
        IReadOnlyList<CompilerDiagnostic> diagnostics)
    {
        Success = success;
        Code = code;
        Pe = pe;
        SourceSha256 = sourceSha256;
        CacheKey = cacheKey;
        Api = api;
        Diagnostics = diagnostics;
    }

    public bool Success { get; }

    public string Code { get; }

    public byte[]? Pe { get; }

    public string SourceSha256 { get; }

    public string CacheKey { get; }

    public RevitApiIdentity? Api { get; }

    public IReadOnlyList<CompilerDiagnostic> Diagnostics { get; }

    public static CSharpQueryCompilation Failed(string code, string source, RevitApiIdentity? api)
    {
        var text = source ?? "";
        var identity = api ?? new RevitApiIdentity("", "", "", 0);
        return new CSharpQueryCompilation(
            false,
            code,
            null,
            text.Length == 0 ? "" : CompilerHashes.SourceSha256(text),
            text.Length == 0 ? "" : CompilerHashes.CacheKey(text, identity, CompilerProtocol.PolicyVersion),
            api,
            []);
    }

    public static CSharpQueryCompilation From(CompilerCallResult result)
    {
        var response = result.Response;
        return new CSharpQueryCompilation(
            result.Accepted && response?.Outcome == CompilerOutcomes.Success,
            result.Code,
            result.Accepted && response?.Outcome == CompilerOutcomes.Success ? response.Pe : null,
            response?.SourceSha256 ?? "",
            response?.CacheKey ?? "",
            response?.RevitApi,
            response?.Diagnostics ?? []);
    }
}

internal sealed class LoadedRevitApi
{
    public LoadedRevitApi(string path, RevitApiIdentity identity)
    {
        Path = path;
        Identity = identity;
    }

    public string Path { get; }

    public RevitApiIdentity Identity { get; }

    public static LoadedRevitApi FromCurrentProcess()
    {
        var assembly = typeof(Autodesk.Revit.DB.Document).Assembly;
        var location = assembly.Location;
        if (string.IsNullOrWhiteSpace(location))
        {
            throw new InvalidOperationException("The loaded Revit API assembly has no location.");
        }

        return new LoadedRevitApi(location, RevitApiIdentity.FromFile(location));
    }
}

internal sealed class CSharpQueryCompilationService
{
    public CSharpQueryCompilation Compile(string source)
    {
        return Compile(source, LoadedRevitApi.FromCurrentProcess(), typeof(CSharpQueryCompilationService).Assembly.Location);
    }

    public CSharpQueryCompilation Compile(string source, LoadedRevitApi api, string productAssemblyLocation)
    {
        return Compile(source, api, productAssemblyLocation, CompilerProtocol.TimeoutMilliseconds);
    }

    internal CSharpQueryCompilation Compile(string source, LoadedRevitApi api, string productAssemblyLocation, int timeoutMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(api);
        if (string.IsNullOrWhiteSpace(source) || source.Length > CompilerProtocol.MaxSourceCharacters)
        {
            return CSharpQueryCompilation.Failed(CompilerResultCodes.InvalidSource, source ?? "", api.Identity);
        }

        if (!api.Identity.IsPolicyV1Supported)
        {
            return CSharpQueryCompilation.Failed(CompilerResultCodes.ApiUnsupported, source, api.Identity);
        }

        if (!CompilerHostLocation.TryResolve(productAssemblyLocation, out var executable))
        {
            return CSharpQueryCompilation.Failed(CompilerResultCodes.CompilerFailed, source, api.Identity);
        }

        var request = new CompilerRequest
        {
            ContractVersion = CompilerProtocol.ContractVersion,
            PolicyVersion = CompilerProtocol.PolicyVersion,
            Source = source,
            RevitApiPath = api.Path,
            RevitApi = api.Identity
        };
        var result = CompilerHostClient.Run(executable, request, timeoutMilliseconds);
        return CSharpQueryCompilation.From(result);
    }
}
