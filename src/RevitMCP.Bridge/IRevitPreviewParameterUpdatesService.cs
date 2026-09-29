using RevitMCP.Contracts;

namespace RevitMCP.Bridge;

public interface IRevitPreviewParameterUpdatesService
{
    Task<PreviewParameterUpdatesResult> PreviewParameterUpdatesAsync(
        PreviewParameterUpdatesRequest request,
        CancellationToken cancellationToken);
}
