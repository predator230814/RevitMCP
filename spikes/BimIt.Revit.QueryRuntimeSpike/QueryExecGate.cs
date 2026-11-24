using System.Text.Json.Serialization;

namespace BimIt.Revit.QueryRuntimeSpike;

/// <summary>
/// Fixed spike queries. The transaction sample is compiled for rejection and is never an execution.
/// </summary>
public sealed class QueryExecGate
{
    public const int TightLoopBudget = 5;

    private QueryExecGate(QueryCompilation levelQuery, QueryCompilation tightLoop, QueryCompilation transactionAttempt)
    {
        LevelQuery = levelQuery;
        TightLoop = tightLoop;
        TransactionAttempt = transactionAttempt;
    }

    public QueryCompilation LevelQuery { get; }

    public QueryCompilation TightLoop { get; }

    public QueryCompilation TransactionAttempt { get; }

    public bool TransactionExecutionEnqueued => false;

    public static QueryExecGate Prepare(QueryCompiler compiler)
    {
        ArgumentNullException.ThrowIfNull(compiler);
        return ForCompilations(
            compiler.Compile(QueryRuntimeSamples.LevelProjection),
            compiler.Compile(QueryRuntimeSamples.TightLoop),
            compiler.Compile(QueryRuntimeSamples.TransactionAttempt));
    }

    public static QueryExecGate ForCompilations(
        QueryCompilation levelQuery,
        QueryCompilation tightLoop,
        QueryCompilation transactionAttempt)
    {
        ArgumentNullException.ThrowIfNull(levelQuery);
        ArgumentNullException.ThrowIfNull(tightLoop);
        ArgumentNullException.ThrowIfNull(transactionAttempt);
        return new QueryExecGate(levelQuery, tightLoop, transactionAttempt);
    }
}

public sealed class QueryExecStepResult
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("success")]
    public required bool Success { get; init; }

    [JsonPropertyName("json")]
    public required string? Json { get; init; }

    [JsonPropertyName("cache_key")]
    public required string CacheKey { get; init; }

    [JsonPropertyName("code")]
    public required string? Code { get; init; }

    [JsonPropertyName("modified_before")]
    public required bool ModifiedBefore { get; init; }

    [JsonPropertyName("modified_after")]
    public required bool ModifiedAfter { get; init; }

    [JsonPropertyName("context_collected")]
    public required bool ContextCollected { get; init; }
}

public static class QueryExecSequence
{
    public static IReadOnlyList<QueryExecStepResult> Run(
        QueryExecGate gate,
        Func<QueryCompilation, int, QueryExecutionEvidence> execute,
        Func<bool> isModified)
    {
        ArgumentNullException.ThrowIfNull(gate);
        ArgumentNullException.ThrowIfNull(execute);
        ArgumentNullException.ThrowIfNull(isModified);
        if (gate.TransactionExecutionEnqueued || gate.TransactionAttempt.Success || gate.TransactionAttempt.Assembly is not null)
        {
            throw new InvalidOperationException("A rejected query must not be executable.");
        }

        return
        [
            RunOne("level", gate.LevelQuery, QueryPolicy.DefaultBudget, execute, isModified),
            RunOne("level-repeat", gate.LevelQuery, QueryPolicy.DefaultBudget, execute, isModified),
            RunOne("tight-loop", gate.TightLoop, QueryExecGate.TightLoopBudget, execute, isModified),
            RunOne("level-after-budget", gate.LevelQuery, QueryPolicy.DefaultBudget, execute, isModified)
        ];
    }

    public static bool TryRelease(WeakReference? context)
    {
        if (context is null)
        {
            return false;
        }

        for (var attempt = 0; attempt < 10 && context.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        return !context.IsAlive;
    }

    private static QueryExecStepResult RunOne(
        string name,
        QueryCompilation compilation,
        int budget,
        Func<QueryCompilation, int, QueryExecutionEvidence> execute,
        Func<bool> isModified)
    {
        if (!compilation.Success || compilation.Assembly is null)
        {
            return new QueryExecStepResult
            {
                Name = name,
                Success = false,
                Json = null,
                CacheKey = compilation.CacheKey,
                Code = compilation.Diagnostics.FirstOrDefault()?.Code,
                ModifiedBefore = isModified(),
                ModifiedAfter = isModified(),
                ContextCollected = false
            };
        }

        var before = isModified();
        var evidence = execute(compilation, budget);
        var after = isModified();
        var collected = TryRelease(evidence.Context);
        return new QueryExecStepResult
        {
            Name = name,
            Success = evidence.Result.Success,
            Json = evidence.Result.Json,
            CacheKey = evidence.Result.CacheKey,
            Code = evidence.Result.Diagnostics.FirstOrDefault()?.Code,
            ModifiedBefore = before,
            ModifiedAfter = after,
            ContextCollected = collected
        };
    }
}
