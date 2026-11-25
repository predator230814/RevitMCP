using BimIt.Revit.Contracts;

namespace BimIt.Revit.Bridge;

public interface IRevitRunCSharpQueryService
{
    Task<RunCSharpQueryResult> RunCSharpQueryAsync(
        RunCSharpQueryRequest request,
        CancellationToken cancellationToken);
}
