using System.Text.Json;
using Xunit;
using BimIt.Revit.Addin.CSharpQuery;
using BimIt.Revit.Bridge;
using BimIt.Revit.Compiler;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Addin.Tests;

public sealed class RunCSharpQuerySliceTests
{
    [Fact]
    public void Missing_malformed_unreadable_and_wrong_schema_stay_disabled()
    {
        var root = NewRoot();
        var store = new CSharpQueryEnablementStore(root);
        Assert.False(store.IsEnabled());

        Write(root, "{");
        Assert.False(store.IsEnabled());

        Write(root, """{"schema_version":2,"csharp_query_enabled":true}""");
        Assert.False(store.IsEnabled());

        Write(root, """{"schema_version":"1","csharp_query_enabled":true}""");
        Assert.False(store.IsEnabled());

        Write(root, """{"schema_version":1,"csharp_query_enabled":"true"}""");
        Assert.False(store.IsEnabled());

        Write(root, """{"schema_version":1,"csharp_query_enabled":true,"extra":1}""");
        Assert.False(store.IsEnabled());

        Write(root, """{"schema_version":1}""");
        Assert.False(store.IsEnabled());

        Write(root, """{"csharp_query_enabled":true}""");
        Assert.False(store.IsEnabled());

        Write(root, """{"schema_version":1,"csharp_query_enabled":false}""");
        Assert.False(store.IsEnabled());

        Write(root, """{"schema_version":1,"csharp_query_enabled":true}""");
        var path = Path.Combine(root, CSharpQueryEnablementStore.FileName);
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.False(store.IsEnabled());
        }

        Assert.True(store.IsEnabled());
    }

    [Fact]
    public async Task Enablement_is_reread_and_disabled_does_not_compile_or_execute()
    {
        var root = NewRoot();
        var compiler = new RecordingCompiler();
        var executor = new RecordingExecutor();
        var service = new RevitRunCSharpQueryService(new CSharpQueryEnablementStore(root), compiler, executor);

        var disabled = await Assert.ThrowsAsync<BridgeException>(() => service.RunCSharpQueryAsync(Valid(), CancellationToken.None));
        Assert.Equal("CSHARP_QUERY_DISABLED", disabled.ErrorCode);
        Assert.Equal(0, compiler.Calls);
        Assert.Equal(0, executor.Calls);

        Write(root, """{"schema_version":1,"csharp_query_enabled":true}""");
        compiler.Result = SuccessCompilation();
        executor.Result = SuccessExecution(compiler.Result);
        var result = await service.RunCSharpQueryAsync(Valid(), CancellationToken.None);
        Assert.Equal(1, result.Result.GetInt32());
        Assert.Equal("abc", result.SourceSha256);
        Assert.Equal("cache", result.CacheKey);
        Assert.Equal("RevitAPI", result.RevitApi.Name);
        Assert.Equal("26.5.0.0", result.RevitApi.Version);
        Assert.DoesNotContain("source text", result.Result.GetRawText(), StringComparison.Ordinal);

        Write(root, """{"schema_version":1,"csharp_query_enabled":false}""");
        var again = await Assert.ThrowsAsync<BridgeException>(() => service.RunCSharpQueryAsync(Valid(), CancellationToken.None));
        Assert.Equal("CSHARP_QUERY_DISABLED", again.ErrorCode);
        Assert.Equal(1, compiler.Calls);
        Assert.Equal(1, executor.Calls);
    }

    [Theory]
    [InlineData(null, "return 1;")]
    [InlineData(" ", "return 1;")]
    [InlineData("doc", null)]
    [InlineData("doc", "")]
    [InlineData("doc", " ")]
    public async Task Missing_or_blank_envelope_is_rejected_before_compile(string? documentId, string? source)
    {
        var compiler = new RecordingCompiler();
        var service = Enabled(compiler, new RecordingExecutor());
        var error = await Assert.ThrowsAsync<BridgeException>(() => service.RunCSharpQueryAsync(
            new RunCSharpQueryRequest { DocumentId = documentId, Source = source },
            CancellationToken.None));
        Assert.Equal("INVALID_CSHARP_QUERY", error.ErrorCode);
        Assert.Equal(0, compiler.Calls);
    }

    [Fact]
    public async Task Oversized_source_and_unknown_fields_are_rejected()
    {
        var compiler = new RecordingCompiler();
        var service = Enabled(compiler, new RecordingExecutor());
        var oversized = await Assert.ThrowsAsync<BridgeException>(() => service.RunCSharpQueryAsync(
            new RunCSharpQueryRequest { DocumentId = "doc", Source = new string('a', 8001) },
            CancellationToken.None));
        Assert.Equal("INVALID_CSHARP_QUERY", oversized.ErrorCode);

        var extra = await Assert.ThrowsAsync<BridgeException>(() => service.RunCSharpQueryAsync(
            new RunCSharpQueryRequest
            {
                DocumentId = "doc",
                Source = "return 1;",
                ExtensionData = new Dictionary<string, JsonElement> { ["references"] = default }
            },
            CancellationToken.None));
        Assert.Equal("INVALID_CSHARP_QUERY", extra.ErrorCode);
        Assert.Equal(0, compiler.Calls);
    }

    [Fact]
    public async Task Compiler_failure_keeps_bounded_diagnostics_and_does_not_execute()
    {
        var compiler = new RecordingCompiler
        {
            Result = CSharpQueryCompilation.From(CompilerCallResult.Ok(new CompilerResponse
            {
                Outcome = CompilerOutcomes.PolicyRejected,
                PolicyVersion = 1,
                SourceSha256 = "hash",
                CacheKey = "hidden-cache",
                RevitApi = new RevitApiIdentity("RevitAPI", "26.5.0.0", "token", 10),
                Diagnostics = Enumerable.Range(0, 10).Select(index => new CompilerDiagnostic("CS" + index, index, 1, "bad")).ToArray()
            }))
        };
        var executor = new RecordingExecutor();
        var error = await Assert.ThrowsAsync<BridgeException>(() =>
            Enabled(compiler, executor).RunCSharpQueryAsync(Valid(), CancellationToken.None));
        Assert.Equal(CompilerResultCodes.PolicyRejected, error.ErrorCode);
        Assert.Equal(0, executor.Calls);
        var details = error.Details!.Value;
        Assert.Equal("hash", details.GetProperty("source_sha256").GetString());
        Assert.Equal(1, details.GetProperty("policy_version").GetInt32());
        Assert.Equal("RevitAPI", details.GetProperty("revit_api").GetProperty("name").GetString());
        Assert.Equal(8, details.GetProperty("diagnostics").GetArrayLength());
        Assert.False(details.TryGetProperty("cache_key", out _));
        Assert.False(details.TryGetProperty("source", out _));
        Assert.DoesNotContain("token", details.GetRawText(), StringComparison.Ordinal);
    }

    private static RevitRunCSharpQueryService Enabled(RecordingCompiler compiler, RecordingExecutor executor)
    {
        var root = NewRoot();
        Write(root, """{"schema_version":1,"csharp_query_enabled":true}""");
        return new RevitRunCSharpQueryService(new CSharpQueryEnablementStore(root), compiler, executor);
    }

    private static RunCSharpQueryRequest Valid() => new() { DocumentId = "doc", Source = "return 1;" };

    private static CSharpQueryCompilation SuccessCompilation()
    {
        return CSharpQueryCompilation.From(CompilerCallResult.Ok(new CompilerResponse
        {
            Outcome = CompilerOutcomes.Success,
            PolicyVersion = 1,
            SourceSha256 = "abc",
            CacheKey = "cache",
            RevitApi = new RevitApiIdentity("RevitAPI", "26.5.0.0", "", 4),
            Pe = new byte[64]
        }));
    }

    private static CSharpQueryExecution SuccessExecution(CSharpQueryCompilation compilation)
    {
        var copied = CSharpQueryResultCopier.Copy(1, null);
        return CSharpQueryExecution.Succeeded(copied, 4).WithCompilation(compilation);
    }

    private static string NewRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "revitmcp-csharp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void Write(string root, string json)
    {
        File.WriteAllText(Path.Combine(root, CSharpQueryEnablementStore.FileName), json);
    }

    private sealed class RecordingCompiler : ICSharpQueryCompiler
    {
        public int Calls { get; private set; }

        public CSharpQueryCompilation Result { get; set; } = CSharpQueryCompilation.Failed("INVALID_CSHARP_QUERY", "", null);

        public CSharpQueryCompilation Compile(string source)
        {
            Calls++;
            return Result;
        }
    }

    private sealed class RecordingExecutor : ICSharpQueryExecutor
    {
        public int Calls { get; private set; }

        public CSharpQueryExecution Result { get; set; } = CSharpQueryExecution.Failed("CSHARP_QUERY_EXECUTION_FAILED", null, 1);

        public Task<CSharpQueryExecution> ExecuteAsync(string documentId, CSharpQueryCompilation compilation, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(Result);
        }
    }
}
