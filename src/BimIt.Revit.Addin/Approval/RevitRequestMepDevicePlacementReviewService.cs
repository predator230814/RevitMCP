using BimIt.Revit.Addin.Intents;
using BimIt.Revit.Bridge;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Addin.Approval;

internal sealed class RevitRequestMepDevicePlacementReviewService : IRevitRequestMepDevicePlacementReviewService
{
    private readonly ApprovalUiRuntime _runtime;

    public RevitRequestMepDevicePlacementReviewService(ApprovalUiRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        _runtime = runtime;
    }

    public async Task<RequestMepDevicePlacementReviewResult> RequestMepDevicePlacementReviewAsync(
        RequestMepDevicePlacementReviewRequest request,
        CancellationToken cancellationToken)
    {
        RequestMepDevicePlacementReviewRequests.Validate(request);
        var presented = await _runtime
            .PresentReviewAsync(request.IntentRef, ControlledWriteIntentKind.MepDevicePlacements, cancellationToken)
            .ConfigureAwait(false);
        return new RequestMepDevicePlacementReviewResult { Status = Map(presented.Status) };
    }

    private static RequestMepDevicePlacementReviewStatus Map(ApprovalReviewStatus status)
    {
        return status switch
        {
            ApprovalReviewStatus.Started => RequestMepDevicePlacementReviewStatus.Started,
            ApprovalReviewStatus.AlreadyActive => RequestMepDevicePlacementReviewStatus.AlreadyActive,
            ApprovalReviewStatus.Busy => RequestMepDevicePlacementReviewStatus.Busy,
            ApprovalReviewStatus.Terminal => RequestMepDevicePlacementReviewStatus.Terminal,
            _ => RequestMepDevicePlacementReviewStatus.Unavailable
        };
    }
}
