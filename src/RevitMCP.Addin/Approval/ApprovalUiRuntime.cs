namespace RevitMCP.Addin.Approval;

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
    private ApprovalReviewRenderModel? _current;
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

    public void RegisterSurface(IApprovalPaneSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        _surface = surface;
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
        var adapter = _adapter;
        if (_stopped || adapter is null)
        {
            return Task.FromResult(new ApprovalPresentationResult(ApprovalReviewStatus.Unavailable, null));
        }

        return adapter.PresentReviewAsync(intentRef, cancellationToken);
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

    public bool TryPresent(ApprovalReviewRenderModel model, Func<bool> showPane)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(showPane);
        if (_stopped || _unavailable)
        {
            return false;
        }

        _current = model;
        _showRequested = true;
        var shown = false;
        try
        {
            shown = showPane();
        }
        catch (Exception)
        {
            shown = false;
        }

        if (!shown || _stopped)
        {
            _current = null;
            _showRequested = false;
            return false;
        }

        _surface?.Post(ApprovalReviewJson.SerializeRender(model));
        return true;
    }

    public void ClearReview()
    {
        _current = null;
        _showRequested = false;
        _surface?.Post(ApprovalReviewJson.SerializeClear());
    }

    public void NotifyInitializationFailed() => DismissPendingWithoutDecision();

    public void NotifyHostDisposed()
    {
        _surface = null;
        DismissPendingWithoutDecision();
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

    private void DismissPendingWithoutDecision()
    {
        var session = _current?.SessionRef;
        _current = null;
        _showRequested = false;
        if (_stopped || _controller is null || session is null)
        {
            return;
        }

        _controller.DismissCurrent(session);
        _surface?.Post(ApprovalReviewJson.SerializeClear());
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
