using RevitMCP.Bridge;
using RevitMCP.Contracts;

namespace RevitMCP.Addin.Approval;

/// <summary>
/// Thin Bridge adapter over the process-owned approval UI runtime.
/// It does not own an intent store, approval provider, interaction controller, or UI runtime.
/// </summary>
internal sealed class RevitRequestParameterUpdateReviewService : IRevitRequestParameterUpdateReviewService
{
    private readonly ApprovalUiRuntime _runtime;

    public RevitRequestParameterUpdateReviewService(ApprovalUiRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        _runtime = runtime;
    }

    internal ApprovalUiRuntime Runtime => _runtime;

    public async Task<RequestParameterUpdateReviewResult> RequestParameterUpdateReviewAsync(
        RequestParameterUpdateReviewRequest request,
        CancellationToken cancellationToken)
    {
        RequestParameterUpdateReviewRequests.Validate(request);
        var presented = await _runtime.PresentReviewAsync(request.IntentRef, cancellationToken).ConfigureAwait(false);
        return new RequestParameterUpdateReviewResult
        {
            Status = Map(presented.Status)
        };
    }

    private static RequestParameterUpdateReviewStatus Map(ApprovalReviewStatus status)
    {
        return status switch
        {
            ApprovalReviewStatus.Started => RequestParameterUpdateReviewStatus.Started,
            ApprovalReviewStatus.AlreadyActive => RequestParameterUpdateReviewStatus.AlreadyActive,
            ApprovalReviewStatus.Busy => RequestParameterUpdateReviewStatus.Busy,
            ApprovalReviewStatus.Terminal => RequestParameterUpdateReviewStatus.Terminal,
            _ => RequestParameterUpdateReviewStatus.Unavailable
        };
    }
}
