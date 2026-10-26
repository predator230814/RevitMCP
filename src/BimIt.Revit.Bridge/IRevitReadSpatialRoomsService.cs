using BimIt.Revit.Contracts;

namespace BimIt.Revit.Bridge;

public interface IRevitReadSpatialRoomsService
{
    Task<ReadSpatialRoomsResult> ReadSpatialRoomsAsync(
        ReadSpatialRoomsRequest request,
        CancellationToken cancellationToken);
}
