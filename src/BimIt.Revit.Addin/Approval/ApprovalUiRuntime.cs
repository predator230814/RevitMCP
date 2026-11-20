using BimIt.Revit.Addin.Intents;

namespace BimIt.Revit.Addin.Approval;

internal enum ApprovalPresentationRole
{
    None,
    Primary,
    Fallback,
}

internal interface IApprovalPaneSurface
{
    void BeginAfterShow();

    void Post(string json);

    void DisposeSurface();
}

/// <summary>
/// Process-owned approval presentation state. It is not a static singleton.
/// It coordinates the pane host and the current transient render model.
/// It does not own the intent store, provider, or interaction controller.
/// </summary>
internal sealed class ApprovalUiRuntime
{
    private RevitLocalApprovalPresentationAdapter? _adapter;
    private RevitLocalApprovalInteractionController? _controller;
    private IApprovalPaneSurface? _surface;
    private IApprovalPresentationTarget? _primary;
    private IApprovalPresentationTarget? _fallback;
    private ApprovalReviewRenderModel? _current;
    private ApprovalPresentationRole _presentedOn;
    private bool _unavailable;
    private bool _stopped;
    private bool _showRequested;
    private int _actionInFlight;

    public bool IsUnavailable => _unavailable;

    public bool IsStopped => _stopped;

    public bool ShowRequested => _showRequested;

    public ApprovalReviewRenderModel? Current => _current;

    public string? CurrentRenderJson => _current is null || _stopped ? null : ApprovalReviewJson.SerializeRender(_current);

    public void MarkUnavailable() => _unavailable = true;

    public void RegisterPrimary(IApprovalPresentationTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        _primary = target;
    }

    public void RegisterSurface(IApprovalPaneSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        _surface = surface;
        _fallback = new ApprovalPanePresentationTarget(surface);
    }

    public void Attach(RevitLocalApprovalPresentationAdapter adapter, RevitLocalApprovalInteractionController controller)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentNullException.ThrowIfNull(controller);
        _adapter = adapter;
        _controller = controller;
    }

    public void Detach()
    {
        _stopped = true;
        _current = null;
        _showRequested = false;
        _adapter = null;
        _controller = null;
    }

    public void Shutdown()
    {
        Detach();
        var surface = _surface;
        _surface = null;
        surface?.DisposeSurface();
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
        var adapter = _adapter;
        if (_stopped || adapter is null)
        {
            return Task.FromResult(new ApprovalPresentationResult(ApprovalReviewStatus.Unavailable, null));
        }

        return adapter.PresentReviewAsync(intentRef, expectedKind, cancellationToken);
    }

    public void ReceivePageMessage(string? json)
    {
        var adapter = _adapter;
        if (_stopped || adapter is null)
        {
            return;
        }

        var task = adapter.DispatchCurrentAsync(json, CancellationToken.None);
        _ = task.ContinueWith(
            static (completed, state) =>
            {
                if (completed.IsFaulted && state is ApprovalUiRuntime runtime)
                {
                    runtime._surface?.Post(ApprovalReviewJson.SerializeDispatch(ApprovalUiDispatchResult.Unavailable(reviewRemains: false)));
                }
            },
            this,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    public void BeginAfterShow() => _surface?.BeginAfterShow();

    public bool TryPresent(ApprovalReviewRenderModel model, Func<bool> showPrimary, Func<bool> showFallback)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(showPrimary);
        ArgumentNullException.ThrowIfNull(showFallback);
        if (_stopped || _unavailable)
        {
            return false;
        }

        _current = model;
        _showRequested = true;
        var json = ApprovalReviewJson.SerializeRender(model);
        if (TryOne(_primary, showPrimary, model, json, ApprovalPresentationRole.Primary))
        {
            _fallback?.Clear();
            return true;
        }

        if (TryOne(_fallback, showFallback, model, json, ApprovalPresentationRole.Fallback))
        {
            _primary?.Clear();
            return true;
        }

        if (_current is null || string.Equals(_current.SessionRef, model.SessionRef, StringComparison.Ordinal))
        {
            _current = null;
            _showRequested = false;
            _presentedOn = ApprovalPresentationRole.None;
        }

        return false;
    }

    public void ClearReview()
    {
        _current = null;
        _showRequested = false;
        _presentedOn = ApprovalPresentationRole.None;
        _primary?.Clear();
        _fallback?.Clear();
    }

    public void NotifyInitializationFailed()
    {
        if (_presentedOn != ApprovalPresentationRole.Primary)
        {
            DismissPendingWithoutDecision();
        }
    }

    public void NotifyHostDisposed()
    {
        _surface = null;
        _fallback = null;
        if (_presentedOn == ApprovalPresentationRole.Fallback)
        {
            DismissPendingWithoutDecision();
        }
    }

    public ApprovalUiDispatchResult Dispatch(string? json, string? activeDocumentId)
    {
        if (_stopped || _controller is null)
        {
            return ApprovalUiDispatchResult.Unavailable(reviewRemains: false);
        }

        if (Volatile.Read(ref _actionInFlight) != 0)
        {
            return Post(ApprovalUiDispatchResult.Rejected());
        }

        var parsed = ApprovalMessageParser.Parse(json);
        if (!parsed.Accepted || parsed.SessionRef is null)
        {
            return Post(ApprovalUiDispatchResult.Rejected());
        }

        if (_current is null || !string.Equals(parsed.SessionRef, _current.SessionRef, StringComparison.Ordinal))
        {
            return Post(ApprovalUiDispatchResult.Rejected());
        }

        if (Interlocked.Exchange(ref _actionInFlight, 1) != 0)
        {
            return Post(ApprovalUiDispatchResult.Rejected());
        }

        try
        {
            var result = parsed.Kind switch
            {
                ApprovalInboundKind.ApproveCurrent => Map(_controller.ApproveCurrent(parsed.SessionRef, activeDocumentId)),
                ApprovalInboundKind.RejectCurrent => Map(_controller.RejectCurrent(parsed.SessionRef, activeDocumentId)),
                ApprovalInboundKind.DismissCurrent => MapDismiss(_controller.DismissCurrent(parsed.SessionRef)),
                _ => ApprovalUiDispatchResult.Rejected(),
            };

            if (!result.ReviewRemains)
            {
                ClearReview();
            }

            return Post(result);
        }
        finally
        {
            Interlocked.Exchange(ref _actionInFlight, 0);
        }
    }

    private bool TryOne(
        IApprovalPresentationTarget? target,
        Func<bool> show,
        ApprovalReviewRenderModel model,
        string json,
        ApprovalPresentationRole role)
    {
        if (target is null)
        {
            return false;
        }

        var shown = false;
        try
        {
            shown = show();
        }
        catch (Exception)
        {
            shown = false;
        }

        if (!shown || _stopped || _current is null || !string.Equals(_current.SessionRef, model.SessionRef, StringComparison.Ordinal))
        {
            return false;
        }

        target.Publish(json);
        _presentedOn = role;
        return true;
    }

    private void DismissPendingWithoutDecision()
    {
        var session = _current?.SessionRef;
        _current = null;
        _showRequested = false;
        _presentedOn = ApprovalPresentationRole.None;
        if (_stopped || _controller is null || session is null)
        {
            return;
        }

        _controller.DismissCurrent(session);
        _primary?.Clear();
        _fallback?.Clear();
    }

    private sealed class ApprovalPanePresentationTarget : IApprovalPresentationTarget
    {
        private readonly IApprovalPaneSurface _surface;

        public ApprovalPanePresentationTarget(IApprovalPaneSurface surface)
        {
            _surface = surface;
        }

        public void Publish(string json) => _surface.Post(json);

        public void Clear() => _surface.Post(ApprovalReviewJson.SerializeClear());
    }

    private ApprovalUiDispatchResult Post(ApprovalUiDispatchResult result)
    {
        _surface?.Post(ApprovalReviewJson.SerializeDispatch(result));
        return result;
    }

    private static ApprovalUiDispatchResult Map(ApprovalCommandResult result)
    {
        return result.Status switch
        {
            ApprovalCommandStatus.Recorded => ApprovalUiDispatchResult.Recorded(),
            ApprovalCommandStatus.InvalidSession => ApprovalUiDispatchResult.InvalidSession(),
            _ => ApprovalUiDispatchResult.Unavailable(reviewRemains: false),
        };
    }

    private static ApprovalUiDispatchResult MapDismiss(ApprovalDismissResult result)
    {
        return result.Status == ApprovalDismissStatus.Dismissed
            ? ApprovalUiDispatchResult.Dismissed()
            : ApprovalUiDispatchResult.InvalidSession();
    }
}
