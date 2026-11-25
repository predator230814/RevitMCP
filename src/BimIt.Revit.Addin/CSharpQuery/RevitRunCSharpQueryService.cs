using System.Text.Json;
using BimIt.Revit.Bridge;
using BimIt.Revit.Compiler;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Addin.CSharpQuery;

internal sealed class RevitRunCSharpQueryService : IRevitRunCSharpQueryService
{
    private readonly CSharpQueryEnablementStore _enablement;
    private readonly ICSharpQueryCompiler _compiler;
    private readonly ICSharpQueryExecutor _executor;

    public RevitRunCSharpQueryService(
        CSharpQueryEnablementStore enablement,
        ICSharpQueryCompiler compiler,
        ICSharpQueryExecutor executor)
    {
        ArgumentNullException.ThrowIfNull(enablement);
        ArgumentNullException.ThrowIfNull(compiler);
        ArgumentNullException.ThrowIfNull(executor);
        _enablement = enablement;
        _compiler = compiler;
        _executor = executor;
    }

    public async Task<RunCSharpQueryResult> RunCSharpQueryAsync(
        RunCSharpQueryRequest request,
        CancellationToken cancellationToken)
    {
        if (!_enablement.IsEnabled())
        {
            throw Reject("CSHARP_QUERY_DISABLED", null);
        }

        if (!TryValidate(request, out var documentId, out var source))
        {
            throw Reject("INVALID_CSHARP_QUERY", null);
        }

        var compilation = _compiler.Compile(source);
        if (!compilation.Success || compilation.Pe is null)
        {
            throw Reject(string.IsNullOrWhiteSpace(compilation.Code) ? "CSHARP_QUERY_COMPILER_FAILED" : compilation.Code, DetailsFrom(compilation, null));
        }

        var execution = await _executor.ExecuteAsync(documentId, compilation, cancellationToken).ConfigureAwait(false);
        if (!execution.Success || string.IsNullOrWhiteSpace(execution.ResultJson))
        {
            throw Reject(
                string.IsNullOrWhiteSpace(execution.Code) ? CSharpQueryCodes.ExecutionFailed : execution.Code,
                DetailsFrom(compilation, execution));
        }

        using var document = JsonDocument.Parse(execution.ResultJson);
        return new RunCSharpQueryResult
        {
            Result = document.RootElement.Clone(),
            SourceSha256 = execution.SourceSha256,
            CacheKey = execution.CacheKey,
            PolicyVersion = execution.PolicyVersion,
            RevitApi = new RunCSharpQueryApiIdentity
            {
                Name = execution.ApiName ?? "",
                Version = execution.ApiVersion ?? ""
            },
            CompileOutcome = execution.CompileOutcome,
            DurationMs = execution.DurationMilliseconds,
            ResultCharacters = execution.ResultCharacters
        };
    }

    private static bool TryValidate(RunCSharpQueryRequest? request, out string documentId, out string source)
    {
        documentId = "";
        source = "";
        if (request is null || request.ExtensionData is { Count: > 0 })
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(request.DocumentId) || string.IsNullOrWhiteSpace(request.Source))
        {
            return false;
        }

        if (request.Source.Length > CompilerProtocol.MaxSourceCharacters)
        {
            return false;
        }

        documentId = request.DocumentId;
        source = request.Source;
        return true;
    }

    private static RunCSharpQueryErrorDetails DetailsFrom(CSharpQueryCompilation compilation, CSharpQueryExecution? execution)
    {
        var diagnostics = compilation.Diagnostics
            .Take(CompilerProtocol.MaxDiagnostics)
            .Select(item => new RunCSharpQueryDiagnostic
            {
                Code = item.Code,
                Line = item.Line,
                Column = item.Column,
                Message = item.Message
            })
            .ToArray();
        return new RunCSharpQueryErrorDetails
        {
            Reason = execution?.Reason,
            SourceSha256 = string.IsNullOrWhiteSpace(execution?.SourceSha256) ? compilation.SourceSha256 : execution.SourceSha256,
            PolicyVersion = CompilerProtocol.PolicyVersion,
            RevitApi = compilation.Api is null && string.IsNullOrWhiteSpace(execution?.ApiName)
                ? null
                : new RunCSharpQueryApiIdentity
                {
                    Name = execution?.ApiName ?? compilation.Api?.Name ?? "",
                    Version = execution?.ApiVersion ?? compilation.Api?.Version ?? ""
                },
            Diagnostics = diagnostics.Length == 0 ? null : diagnostics,
            CompileOutcome = string.IsNullOrWhiteSpace(execution?.CompileOutcome)
                ? compilation.CompileOutcome
                : execution.CompileOutcome
        };
    }

    private static BridgeException Reject(string code, RunCSharpQueryErrorDetails? details)
    {
        const string message = "The C# query could not be completed.";
        if (details is null)
        {
            return new BridgeException(code, message);
        }

        return new BridgeException(code, message, JsonSerializer.SerializeToElement(details, ContractJson.Options));
    }
}
