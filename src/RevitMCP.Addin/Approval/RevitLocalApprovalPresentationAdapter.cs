using Autodesk.Revit.UI;
using RevitMCP.Addin.Execution;
using RevitMCP.Addin.Identity;

namespace RevitMCP.Addin.Approval;

internal interface IApprovalRevitGate
{
    Task<ApprovalPresentationResult> InvokeAsync(
        CancellationToken cancellationToken,
        Func<string?, Func<bool>, ApprovalPresentationResult> present);

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
        return _gate.InvokeAsync(cancellationToken, (documentId, showPane) => PresentResolved(intentRef, documentId, showPane));
    }

    public Task<ApprovalUiDispatchResult> DispatchCurrentAsync(string? json, CancellationToken cancellationToken)
    {
        return _gate.DispatchAsync(cancellationToken, documentId => _ui.Dispatch(json, documentId));
    }

    private ApprovalPresentationResult PresentResolved(string? intentRef, string? activeDocumentId, Func<bool> showPane)
    {
        var review = _controller.BeginReview(intentRef, activeDocumentId);
        if (review.Status is not (ApprovalReviewStatus.Started or ApprovalReviewStatus.AlreadyActive)
            || review.RenderModel is not { } model)
        {
            return new ApprovalPresentationResult(review.Status, null);
        }

        if (!_ui.TryPresent(model, showPane))
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

    public RevitLocalApprovalPresentationGate(
        RevitExecutionDispatcher dispatcher,
        OpenDocumentIdentityService identity,
        ApprovalUiRuntime ui)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(ui);
        _dispatcher = dispatcher;
        _identity = identity;
        _ui = ui;
    }

    public Task<ApprovalPresentationResult> InvokeAsync(
        CancellationToken cancellationToken,
        Func<string?, Func<bool>, ApprovalPresentationResult> present)
    {
        ArgumentNullException.ThrowIfNull(present);
        return _dispatcher.EnqueueAsync(application =>
        {
            var documentId = ResolveActiveDocumentId(application);
            return present(documentId, () => ShowRegisteredPane(application));
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
