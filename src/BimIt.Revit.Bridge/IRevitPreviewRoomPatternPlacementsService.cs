using BimIt.Revit.Contracts;

namespace BimIt.Revit.Bridge;

public interface IRevitPreviewRoomPatternPlacementsService
{
    bool SupportsLinkedRooms => false;

    Task<PreviewRoomPatternPlacementsResult> PreviewRoomPatternPlacementsAsync(
        PreviewRoomPatternPlacementsRequest request,
        CancellationToken cancellationToken);
}
