using BimIt.Revit.Contracts;

namespace BimIt.Revit.Bridge;

public interface IRevitCapabilityService
{
    Task<GetContextResult> GetContextAsync(GetContextRequest request, CancellationToken cancellationToken);
}
