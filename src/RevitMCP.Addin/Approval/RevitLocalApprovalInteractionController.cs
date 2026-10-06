using RevitMCP.Addin.Intents;

namespace RevitMCP.Addin.Approval;

internal readonly record struct ApprovalInteractionReviewResult(
    ApprovalReviewStatus Status,
    ApprovalReviewRenderModel? RenderModel);

/// <summary>
/// Native in-process boundary a future trusted approval UI calls.
/// The render model comes only from Addin-owned immutable intent state.
/// <c>session_ref</c> correlates the current review; it does not authorize a decision.
/// Browser, model, and client data cannot mint an approval.
/// This boundary cannot consume an approval. Future CAP-0008 remains the only consumer of approved state.
/// </summary>
internal sealed class RevitLocalApprovalInteractionController
{
    private readonly EphemeralWriteIntentStore _intents;
    private readonly RevitLocalApprovalProviderStateMachine _provider;

    public RevitLocalApprovalInteractionController(
        EphemeralWriteIntentStore intents,
        RevitLocalApprovalProviderStateMachine provider)
    {
        ArgumentNullException.ThrowIfNull(intents);
        ArgumentNullException.ThrowIfNull(provider);
        _intents = intents;
        _provider = provider;
    }

    public ApprovalInteractionReviewResult BeginReview(string? intentRef, string? activeDocumentId)
    {
        var review = _provider.BeginReview(intentRef, activeDocumentId);
        if (review.Status is not (ApprovalReviewStatus.Started or ApprovalReviewStatus.AlreadyActive))
        {
            return new ApprovalInteractionReviewResult(review.Status, null);
        }

        if (review.SessionRef is null
            || intentRef is null
            || !_intents.TryGet(intentRef, out var entry)
            || entry is null
            || !string.Equals(entry.IntentRef, intentRef, StringComparison.Ordinal)
            || !ApprovalReviewRenderer.TryCreate(review.SessionRef, entry, out var model))
        {
            FailClosed(review.SessionRef);
            return new ApprovalInteractionReviewResult(ApprovalReviewStatus.Unavailable, null);
        }

        return new ApprovalInteractionReviewResult(review.Status, model);
    }

    public ApprovalCommandResult ApproveCurrent(string? sessionRef, string? activeDocumentId)
    {
        return _provider.ApproveCurrent(sessionRef, activeDocumentId);
    }

    public ApprovalCommandResult RejectCurrent(string? sessionRef, string? activeDocumentId)
    {
        return _provider.RejectCurrent(sessionRef, activeDocumentId);
    }

    public ApprovalDismissResult DismissCurrent(string? sessionRef)
    {
        return _provider.DismissCurrent(sessionRef);
    }

    private void FailClosed(string? sessionRef)
    {
        if (sessionRef is not null)
        {
            _provider.DismissCurrent(sessionRef);
        }
    }
}
