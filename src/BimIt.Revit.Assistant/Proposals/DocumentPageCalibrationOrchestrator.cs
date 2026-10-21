namespace BimIt.Revit.Assistant;

internal sealed record CalibrationPagePayload(
    string SessionRef,
    string MimeType,
    string ImageBase64,
    int RasterWidth,
    int RasterHeight,
    string InstanceId,
    string DocumentId,
    string TemplateElementRef);

internal sealed record DocumentCalibrationBegin(CalibrationPagePayload? Page, MepDevicePlacementPreviewOutcome? Outcome)
{
    public static DocumentCalibrationBegin Show(CalibrationPagePayload page) => new(page, null);

    public static DocumentCalibrationBegin Message(MepDevicePlacementPreviewOutcome outcome) => new(null, outcome);
}

/// <summary>
/// Local document-page calibration. The model cannot invoke it. A successful calibration calls the existing preview path once.
/// </summary>
internal sealed class DocumentPageCalibrationOrchestrator
{
    private readonly IAssistantToolSession _session;
    private readonly IMepDevicePlacementPreviewClient _preview;
    private readonly MepDevicePlacementProposalStore _store;
    private readonly AssistantDocumentStore _documents;
    private readonly string _instanceId;
    private readonly Func<DateTimeOffset> _utcNow;

    public DocumentPageCalibrationOrchestrator(
        IAssistantToolSession session,
        IMepDevicePlacementPreviewClient preview,
        MepDevicePlacementProposalStore store,
        AssistantDocumentStore documents,
        string instanceId,
        Func<DateTimeOffset>? utcNow = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _preview = preview ?? throw new ArgumentNullException(nameof(preview));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _documents = documents ?? throw new ArgumentNullException(nameof(documents));
        _instanceId = instanceId ?? throw new ArgumentNullException(nameof(instanceId));
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task<DocumentCalibrationBegin> BeginAsync(CancellationToken cancellationToken)
    {
        var proposal = _store.Current;
        if (proposal is null)
        {
            return DocumentCalibrationBegin.Message(Fail("There is no placement proposal to calibrate."));
        }

        if (!string.Equals(proposal.CoordinateSpace, MepDevicePlacementCoordinateSpaces.DocumentPage, StringComparison.Ordinal))
        {
            return DocumentCalibrationBegin.Message(Fail("This proposal uses project-axis offsets. Use Confirm project axes & preview."));
        }

        if (_store.TryReuseTerminalApply(out _, out _))
        {
            return DocumentCalibrationBegin.Message(Fail(MepDevicePlacementPreviewOrchestrator.TerminalGenerationMessage));
        }

        if (!proposal.HasTemplateBinding || !string.Equals(proposal.InstanceId, _instanceId, StringComparison.Ordinal))
        {
            return DocumentCalibrationBegin.Message(Fail("The proposal has no selected template. Create a new proposal."));
        }

        if (!await BindingStillMatchesAsync(proposal, cancellationToken).ConfigureAwait(false))
        {
            return DocumentCalibrationBegin.Message(Fail("The selected template no longer matches this proposal. Create a new proposal."));
        }

        if (_store.TryReusePreview(_utcNow(), out var existing))
        {
            return DocumentCalibrationBegin.Message(new MepDevicePlacementPreviewOutcome(false, MepDevicePlacementPreviewOrchestrator.Title, existing));
        }

        if (!TryCurrentPage(proposal, out var page))
        {
            return DocumentCalibrationBegin.Message(Fail("The drawing page is no longer available. Create a new proposal."));
        }

        if (!DocumentCalibrationPreview.TryCreate(page, out var mimeType, out var imageBase64))
        {
            return DocumentCalibrationBegin.Message(Fail("The drawing page could not be shown."));
        }

        var session = _store.OpenCalibrationSession(Guid.NewGuid().ToString("N"), _utcNow().Add(DocumentCalibrationSession.Lifetime));
        if (session is null)
        {
            return DocumentCalibrationBegin.Message(Fail("The drawing page is no longer available. Create a new proposal."));
        }

        return DocumentCalibrationBegin.Show(new CalibrationPagePayload(
            session.SessionRef,
            mimeType,
            imageBase64,
            session.RasterWidth,
            session.RasterHeight,
            session.InstanceId,
            session.DocumentId,
            session.TemplateElementRef));
    }

    public async Task<MepDevicePlacementPreviewOutcome> CompleteAsync(
        string? sessionRef,
        double sourceAX,
        double sourceAY,
        double sourceBX,
        double sourceBY,
        double targetDeltaXMetres,
        double targetDeltaYMetres,
        bool cancelled,
        CancellationToken cancellationToken)
    {
        if (!_store.TryReadCalibrationSession(sessionRef, _utcNow(), out var session))
        {
            _store.ForgetCalibrationSession();
            return Fail("The calibration session is no longer valid.");
        }

        if (cancelled)
        {
            _store.ForgetCalibrationSession();
            return Fail("The plan calibration was not completed. No preview was created.");
        }

        var proposal = _store.Current;
        if (proposal is null || !await BindingStillMatchesAsync(proposal, cancellationToken).ConfigureAwait(false))
        {
            _store.ForgetCalibrationSession();
            return Fail("The selected template no longer matches this proposal. Create a new proposal.");
        }

        if (!TryCurrentPage(proposal, out _))
        {
            _store.ForgetCalibrationSession();
            return Fail("The drawing page is no longer available. Create a new proposal.");
        }

        if (!DocumentPageSimilarity.TryPagePixel(sourceAX, sourceAY, session.RasterWidth, session.RasterHeight, out var sourceA)
            || !DocumentPageSimilarity.TryPagePixel(sourceBX, sourceBY, session.RasterWidth, session.RasterHeight, out var sourceB)
            || !DocumentPageSimilarity.TryCreate(sourceA, sourceB, session.RasterWidth, session.RasterHeight, targetDeltaXMetres, targetDeltaYMetres, out var similarity))
        {
            _store.ForgetCalibrationSession();
            return Fail("The calibration points are too close.");
        }

        if (!DocumentPagePlacementTransform.TryTransform(
                similarity,
                session.RasterWidth,
                session.RasterHeight,
                proposal.LengthUnit,
                proposal.Placements,
                out var transformed,
                out var failure))
        {
            _store.ForgetCalibrationSession();
            return Fail(failure switch
            {
                MepDevicePlacementProposalFailures.Range => "The calibrated placements are outside the allowed range.",
                MepDevicePlacementProposalFailures.Duplicate => "The calibrated placements overlap.",
                _ => "The calibration could not be calculated.",
            });
        }

        if (!_store.TryApplyDocumentCalibration(session, transformed, _utcNow()))
        {
            return Fail("The calibration session is no longer valid.");
        }

        var preview = new MepDevicePlacementPreviewOrchestrator(_session, _preview, _store, _instanceId, _utcNow);
        return await preview.PreviewCalibratedDocumentAsync(cancellationToken).ConfigureAwait(false);
    }

    private bool TryCurrentPage(MepDevicePlacementProposal proposal, out AssistantVisualPage page)
    {
        page = null!;
        var artifact = _documents.Current;
        if (artifact is null
            || artifact.IsDisposed
            || string.IsNullOrEmpty(proposal.SourceArtifactId)
            || !string.Equals(artifact.ArtifactId, proposal.SourceArtifactId, StringComparison.Ordinal)
            || proposal.PageNumber is not int pageNumber)
        {
            return false;
        }

        foreach (var candidate in artifact.VisualPages)
        {
            if (candidate.PageNumber == pageNumber
                && candidate.RasterWidth == proposal.RasterWidth
                && candidate.RasterHeight == proposal.RasterHeight
                && candidate.RasterWidth > 0
                && candidate.RasterHeight > 0)
            {
                page = candidate;
                return true;
            }
        }

        return false;
    }

    private async Task<bool> BindingStillMatchesAsync(MepDevicePlacementProposal proposal, CancellationToken cancellationToken)
    {
        AssistantToolCall call;
        try
        {
            call = await _session.CallAsync(
                AssistantTools.GetSelection,
                new Dictionary<string, object?>
                {
                    ["instance_id"] = _instanceId,
                    ["limit"] = MepDevicePlacementSelectionProbe.Limit,
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return false;
        }

        if (call.IsError
            || !MepDevicePlacementSelectionProbe.TryRead(call.Structured, out var documentId, out var selectedCount, out var truncated, out var refs))
        {
            return false;
        }

        return MepDevicePlacementSelectionProbe.IsSameTemplate(proposal, documentId, selectedCount, truncated, refs);
    }

    private static MepDevicePlacementPreviewOutcome Fail(string body)
    {
        return new MepDevicePlacementPreviewOutcome(true, MepDevicePlacementPreviewOrchestrator.Title, body);
    }
}
