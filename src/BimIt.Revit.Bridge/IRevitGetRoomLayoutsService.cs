using BimIt.Revit.Contracts;

namespace BimIt.Revit.Bridge;

public interface IRevitGetRoomLayoutsService
{
    Task<GetRoomLayoutsResult> GetRoomLayoutsAsync(
        GetRoomLayoutsRequest request,
        CancellationToken cancellationToken);
}
