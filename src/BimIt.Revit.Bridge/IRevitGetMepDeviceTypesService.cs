using BimIt.Revit.Contracts;

namespace BimIt.Revit.Bridge;

public interface IRevitGetMepDeviceTypesService
{
    Task<GetMepDeviceTypesResult> GetMepDeviceTypesAsync(
        GetMepDeviceTypesRequest request,
        CancellationToken cancellationToken);
}
