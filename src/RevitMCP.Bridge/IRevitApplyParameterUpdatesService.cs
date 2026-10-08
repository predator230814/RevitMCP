using RevitMCP.Contracts;

namespace RevitMCP.Bridge;

public interface IRevitApplyParameterUpdatesService
{
    Task<ApplyParameterUpdatesResult> ApplyParameterUpdatesAsync(
        ApplyParameterUpdatesRequest request,
        CancellationToken cancellationToken);
}
