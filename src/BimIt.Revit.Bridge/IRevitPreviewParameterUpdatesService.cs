using BimIt.Revit.Contracts;

namespace BimIt.Revit.Bridge;

public interface IRevitPreviewParameterUpdatesService
{
    Task<PreviewParameterUpdatesResult> PreviewParameterUpdatesAsync(
        PreviewParameterUpdatesRequest request,
        CancellationToken cancellationToken);
}
