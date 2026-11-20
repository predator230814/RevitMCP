using Autodesk.Revit.UI;
using BimIt.Revit.Addin.Execution;
using BimIt.Revit.Addin.Identity;
using BimIt.Revit.Addin.Intents;

namespace BimIt.Revit.Addin.Approval;

internal interface IApprovalRevitGate
{
    Task<ApprovalPresentationResult> InvokeAsync(
        CancellationToken cancellationToken,
        Func<string?, Func<bool>, Func<bool>, ApprovalPresentationResult> present);

    Task<ApprovalUiDispatchResult> DispatchAsync(
        CancellationToken cancellationToken,
        Func<string?, ApprovalUiDispatchResult> dispatch);
}

/// <summary>
/// Internal presentation entry for a future request-review caller.
/// It is not a Bridge, Server, or MCP operation and cannot consume an approval.
/// </summary>
internal sealed class RevitLocalApprovalPresentationAdapter
{
    private readonly RevitLocalApprovalInteractionController _controller;
    private readonly ApprovalUiRuntime _ui;
    private readonly IApprovalRevitGate _gate;

    public RevitLocalApprovalPresentationAdapter(
        RevitLocalApprovalInteractionController controller,
        ApprovalUiRuntime ui,
        IApprovalRevitGate gate)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(ui);
        ArgumentNullException.ThrowIfNull(gate);
        _controller = controller;
        _ui = ui;
        _gate = gate;
    }

    public Task<ApprovalPresentationResult> PresentReviewAsync(string? intentRef, CancellationToken cancellationToken)
    {
        return PresentReviewAsync(intentRef, ControlledWriteIntentKind.ParameterUpdates, cancellationToken);
    }

    public Task<ApprovalPresentationResult> PresentReviewAsync(
        string? intentRef,
        ControlledWriteIntentKind expectedKind,
        CancellationToken cancellationToken)
    {
        return _gate.InvokeAsync(
            cancellationToken,
            (documentId, showPrimary, showFallback) => PresentResolved(intentRef, expectedKind, documentId, showPrimary, showFallback));
    }

    public Task<ApprovalUiDispatchResult> DispatchCurrentAsync(string? json, CancellationToken cancellationToken)
    {
        return _gate.DispatchAsync(cancellationToken, documentId => _ui.Dispatch(json, documentId));
    }

    private ApprovalPresentationResult PresentResolved(
        string? intentRef,
        ControlledWriteIntentKind expectedKind,
        string? activeDocumentId,
        Func<bool> showPrimary,
        Func<bool> showFallback)
    {
        var review = _controller.BeginReview(intentRef, activeDocumentId, expectedKind);
        if (review.Status is not (ApprovalReviewStatus.Started or ApprovalReviewStatus.AlreadyActive)
            || review.RenderModel is not { } model)
        {
            return new ApprovalPresentationResult(review.Status, null);
        }

        if (!_ui.TryPresent(model, showPrimary, showFallback))
        {
            _controller.DismissCurrent(model.SessionRef);
            _ui.ClearReview();
            return new ApprovalPresentationResult(ApprovalReviewStatus.Unavailable, null);
        }

        return new ApprovalPresentationResult(review.Status, model.SessionRef);
    }
}

/// <summary>
/// Enters the existing Revit execution dispatcher and resolves the active document
/// with the non-creating identity lookup. JavaScript never supplies that document id.
/// </summary>
internal sealed class RevitLocalApprovalPresentationGate : IApprovalRevitGate
{
    private readonly RevitExecutionDispatcher _dispatcher;
    private readonly OpenDocumentIdentityService _identity;
    private readonly ApprovalUiRuntime _ui;
    private readonly Func<UIApplication, bool>? _showPrimary;

    public RevitLocalApprovalPresentationGate(
        RevitExecutionDispatcher dispatcher,
        OpenDocumentIdentityService identity,
        ApprovalUiRuntime ui,
        Func<UIApplication, bool>? showPrimary = null)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(ui);
        _dispatcher = dispatcher;
        _identity = identity;
        _ui = ui;
        _showPrimary = showPrimary;
    }

    public Task<ApprovalPresentationResult> InvokeAsync(
        CancellationToken cancellationToken,
        Func<string?, Func<bool>, Func<bool>, ApprovalPresentationResult> present)
    {
        ArgumentNullException.ThrowIfNull(present);
        return _dispatcher.EnqueueAsync(application =>
        {
            var documentId = ResolveActiveDocumentId(application);
            return present(documentId, () => ShowPrimary(application), () => ShowRegisteredPane(application));
        }, cancellationToken);
    }

    public Task<ApprovalUiDispatchResult> DispatchAsync(
        CancellationToken cancellationToken,
        Func<string?, ApprovalUiDispatchResult> dispatch)
    {
        ArgumentNullException.ThrowIfNull(dispatch);
        return _dispatcher.EnqueueAsync(application =>
        {
            var documentId = ResolveActiveDocumentId(application);
            return dispatch(documentId);
        }, cancellationToken);
    }

    private string? ResolveActiveDocumentId(UIApplication application)
    {
        var document = application.ActiveUIDocument?.Document;
        if (document is null || !_identity.TryGet(document, out var documentId))
        {
            return null;
        }

        return documentId;
    }

    private bool ShowPrimary(UIApplication application)
    {
        var show = _showPrimary;
        if (show is null)
        {
            return false;
        }

        try
        {
            return show(application);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private bool ShowRegisteredPane(UIApplication application)
    {
        try
        {
            var pane = application.GetDockablePane(new DockablePaneId(ApprovalPaneIds.PaneId));
            pane.Show();
            _ui.BeginAfterShow();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
