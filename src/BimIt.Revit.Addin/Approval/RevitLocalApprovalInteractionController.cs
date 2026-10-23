using BimIt.Revit.Addin.Intents;

namespace BimIt.Revit.Addin.Approval;

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
        return BeginReview(intentRef, activeDocumentId, ControlledWriteIntentKind.ParameterUpdates);
    }

    public ApprovalInteractionReviewResult BeginReview(
        string? intentRef,
        string? activeDocumentId,
        ControlledWriteIntentKind expectedKind)
    {
        var review = _provider.BeginReview(intentRef, activeDocumentId, expectedKind);
        if (review.Status is not (ApprovalReviewStatus.Started or ApprovalReviewStatus.AlreadyActive))
        {
            return new ApprovalInteractionReviewResult(review.Status, null);
        }

        if (review.SessionRef is null
            || intentRef is null
            || !_intents.TryGetControlled(intentRef, out var stored)
            || stored is null
            || stored.Binding.Kind != expectedKind
            || !string.Equals(stored.Binding.IntentRef, intentRef, StringComparison.Ordinal)
            || !TryRender(review.SessionRef, stored, out var model))
        {
            FailClosed(review.SessionRef);
            return new ApprovalInteractionReviewResult(ApprovalReviewStatus.Unavailable, null);
        }

        return new ApprovalInteractionReviewResult(review.Status, model);
    }

    private static bool TryRender(string sessionRef, ControlledWriteIntent stored, out ApprovalReviewRenderModel model)
    {
        if (stored.Binding.Kind == ControlledWriteIntentKind.ParameterUpdates && stored.ParameterUpdates is not null)
        {
            return ApprovalReviewRenderer.TryCreate(sessionRef, stored.ParameterUpdates, out model);
        }

        if (stored.Binding.Kind == ControlledWriteIntentKind.MepDevicePlacements
            && stored.PatternMepPlacements is not null
            && stored.MepDevicePlacements is null)
        {
            return ApprovalReviewRenderer.TryCreatePattern(sessionRef, stored.Binding, stored.PatternMepPlacements, out model);
        }

        if (stored.Binding.Kind == ControlledWriteIntentKind.MepDevicePlacements
            && stored.MepDevicePlacements is not null
            && stored.PatternMepPlacements is null)
        {
            return ApprovalReviewRenderer.TryCreateMep(sessionRef, stored.Binding, stored.MepDevicePlacements, out model);
        }

        model = null!;
        return false;
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
