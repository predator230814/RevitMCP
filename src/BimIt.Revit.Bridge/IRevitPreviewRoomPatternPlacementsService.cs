using BimIt.Revit.Contracts;

namespace BimIt.Revit.Bridge;

public interface IRevitPreviewRoomPatternPlacementsService
{
    Task<PreviewRoomPatternPlacementsResult> PreviewRoomPatternPlacementsAsync(
        PreviewRoomPatternPlacementsRequest request,
        CancellationToken cancellationToken);
}
