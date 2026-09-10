using RevitMCP.Contracts;

namespace RevitMCP.Bridge;

public interface IRevitCapabilityService
{
    Task<GetContextResult> GetContextAsync(GetContextRequest request, CancellationToken cancellationToken);
}
