using BimIt.Revit.Contracts;

namespace BimIt.Revit.Bridge;

public interface IRevitPreviewMepDevicePlacementsService
{
    Task<PreviewMepDevicePlacementsResult> PreviewMepDevicePlacementsAsync(
        PreviewMepDevicePlacementsRequest request,
        CancellationToken cancellationToken);
}
