using RevitMCP.Contracts;

namespace RevitMCP.Bridge;

public interface IRevitGetWarningsService
{
    Task<GetWarningsResult> GetWarningsAsync(
        GetWarningsRequest request,
        CancellationToken cancellationToken);
}
