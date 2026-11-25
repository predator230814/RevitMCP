using BimIt.Revit.Compiler;

namespace BimIt.Revit.Addin.CSharpQuery;

internal sealed class CSharpQueryExecution
{
    private CSharpQueryExecution(
        bool success,
        string code,
        string? reason,
        object? resultValue,
        string? resultJson,
        string sourceSha256,
        string cacheKey,
        int policyVersion,
        string? apiName,
        string? apiVersion,
        string compileOutcome,
        int durationMilliseconds,
        int resultCharacters)
    {
        Success = success;
        Code = code;
        Reason = reason;
        ResultValue = resultValue;
        ResultJson = resultJson;
        SourceSha256 = sourceSha256;
        CacheKey = cacheKey;
        PolicyVersion = policyVersion;
        ApiName = apiName;
        ApiVersion = apiVersion;
        CompileOutcome = compileOutcome;
        DurationMilliseconds = durationMilliseconds;
        ResultCharacters = resultCharacters;
    }

    public bool Success { get; }

    public string Code { get; }

    public string? Reason { get; }

    public object? ResultValue { get; }

    public string? ResultJson { get; }

    public string SourceSha256 { get; }

    public string CacheKey { get; }

    public int PolicyVersion { get; }

    public string? ApiName { get; }

    public string? ApiVersion { get; }

    public string CompileOutcome { get; }

    public int DurationMilliseconds { get; }

    public int ResultCharacters { get; }

    public static CSharpQueryExecution Succeeded(CSharpQueryCopiedResult result, int durationMilliseconds)
    {
        return new CSharpQueryExecution(
            true,
            "",
            null,
            result.Value,
            result.Json,
            "",
            "",
            CompilerProtocol.PolicyVersion,
            null,
            null,
            CompilerOutcomes.Success,
            durationMilliseconds,
            result.Json.Length);
    }

    public static CSharpQueryExecution Failed(string code, string? reason, int durationMilliseconds)
    {
        return new CSharpQueryExecution(
            false,
            code,
            reason,
            null,
            null,
            "",
            "",
            CompilerProtocol.PolicyVersion,
            null,
            null,
            CompilerOutcomes.Success,
            durationMilliseconds,
            0);
    }

    public CSharpQueryExecution WithCompilation(CSharpQueryCompilation compilation)
    {
        return new CSharpQueryExecution(
            Success,
            Code,
            Reason,
            ResultValue,
            ResultJson,
            compilation.SourceSha256,
            compilation.CacheKey,
            CompilerProtocol.PolicyVersion,
            compilation.Api?.Name,
            compilation.Api?.Version,
            compilation.CompileOutcome ?? (compilation.Success ? CompilerOutcomes.Success : compilation.Code),
            DurationMilliseconds,
            ResultCharacters);
    }
}
