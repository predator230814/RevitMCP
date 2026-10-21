namespace BimIt.Revit.Assistant;

internal static class MepDevicePlacementProposalStates
{
    public const string Ready = "ready";

    public const string NeedsCalibration = "needs_calibration";
}

internal static class MepDevicePlacementProposalReasons
{
    public const string CoordinateBasisUnestablished = "coordinate_basis_unestablished";

    public const string TemplateSelectionRequired = "template_selection_required";

    public const string NoActiveDocument = "no_active_document";

    public const string DocumentCalibrationRequired = "document_calibration_required";
}

internal static class MepDevicePlacementCoordinateSpaces
{
    public const string ProjectOffsets = "project_offsets";

    public const string DocumentPage = "document_page";
}

internal sealed record MepDevicePlacementProposalItem(
    double OffsetX,
    double OffsetY,
    double VerticalOffset,
    string? SourceEvidence,
    double? Confidence,
    double? PageX = null,
    double? PageY = null);

/// <summary>
/// One session-local proposal. Revit identities and the source attachment id stay on this object and are omitted from <see cref="ToModelResult"/>.
/// </summary>
internal sealed class MepDevicePlacementProposal
{
    private MepDevicePlacementProposal(
        string coordinateSpace,
        string lengthUnit,
        IReadOnlyList<MepDevicePlacementProposalItem> placements,
        string state,
        string? reason,
        string instanceId,
        string? documentId,
        string? templateElementRef,
        string? sourceArtifactId,
        int? pageNumber,
        int? rasterWidth,
        int? rasterHeight)
    {
        CoordinateSpace = coordinateSpace;
        LengthUnit = lengthUnit;
        Placements = placements;
        State = state;
        Reason = reason;
        InstanceId = instanceId;
        DocumentId = documentId;
        TemplateElementRef = templateElementRef;
        SourceArtifactId = sourceArtifactId;
        PageNumber = pageNumber;
        RasterWidth = rasterWidth;
        RasterHeight = rasterHeight;
    }

    public string CoordinateSpace { get; }

    public string LengthUnit { get; }

    public IReadOnlyList<MepDevicePlacementProposalItem> Placements { get; }

    public string State { get; }

    public string? Reason { get; }

    public string InstanceId { get; }

    public string? DocumentId { get; }

    public string? TemplateElementRef { get; }

    public string? SourceArtifactId { get; }

    public int? PageNumber { get; }

    public int? RasterWidth { get; }

    public int? RasterHeight { get; }

    public bool HasTemplateBinding =>
        !string.IsNullOrEmpty(InstanceId)
        && !string.IsNullOrEmpty(DocumentId)
        && !string.IsNullOrEmpty(TemplateElementRef);

    public static MepDevicePlacementProposal Create(
        string lengthUnit,
        IReadOnlyList<MepDevicePlacementProposalItem> placements,
        string reason,
        string instanceId,
        string? documentId,
        string? templateElementRef,
        string? sourceArtifactId,
        string coordinateSpace = MepDevicePlacementCoordinateSpaces.ProjectOffsets,
        int? pageNumber = null,
        int? rasterWidth = null,
        int? rasterHeight = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lengthUnit);
        ArgumentNullException.ThrowIfNull(placements);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        if (coordinateSpace == MepDevicePlacementCoordinateSpaces.DocumentPage)
        {
            if (pageNumber is not >= 1 || rasterWidth is not >= 1 || rasterHeight is not >= 1)
            {
                throw new ArgumentException("A document-page proposal needs a rendered page.", nameof(pageNumber));
            }
        }
        else if (coordinateSpace != MepDevicePlacementCoordinateSpaces.ProjectOffsets)
        {
            throw new ArgumentException("The coordinate space is not supported.", nameof(coordinateSpace));
        }

        return new MepDevicePlacementProposal(
            coordinateSpace,
            lengthUnit,
            placements.ToArray(),
            MepDevicePlacementProposalStates.NeedsCalibration,
            reason,
            instanceId,
            string.IsNullOrEmpty(documentId) ? null : documentId,
            string.IsNullOrEmpty(templateElementRef) ? null : templateElementRef,
            string.IsNullOrEmpty(sourceArtifactId) ? null : sourceArtifactId,
            coordinateSpace == MepDevicePlacementCoordinateSpaces.DocumentPage ? pageNumber : null,
            coordinateSpace == MepDevicePlacementCoordinateSpaces.DocumentPage ? rasterWidth : null,
            coordinateSpace == MepDevicePlacementCoordinateSpaces.DocumentPage ? rasterHeight : null);
    }

    public MepDevicePlacementProposal AsReady()
    {
        return Copy(MepDevicePlacementProposalStates.Ready, null, Placements);
    }

    public MepDevicePlacementProposal WithReadyOffsets(IReadOnlyList<MepDevicePlacementProposalItem> placements)
    {
        ArgumentNullException.ThrowIfNull(placements);
        return Copy(MepDevicePlacementProposalStates.Ready, null, placements.ToArray());
    }

    public Dictionary<string, object?> ToModelResult()
    {
        var proposal = new Dictionary<string, object?>
        {
            ["coordinate_space"] = CoordinateSpace,
            ["length_unit"] = LengthUnit,
            ["placements"] = Placements.Select(item => ToItem(CoordinateSpace, item)).ToArray(),
            ["state"] = State,
        };
        if (CoordinateSpace == MepDevicePlacementCoordinateSpaces.DocumentPage && PageNumber is int pageNumber)
        {
            proposal["page_number"] = pageNumber;
        }

        if (Reason is not null)
        {
            proposal["reason"] = Reason;
        }

        return new Dictionary<string, object?>
        {
            ["ok"] = true,
            ["proposal"] = proposal,
        };
    }

    private MepDevicePlacementProposal Copy(string state, string? reason, IReadOnlyList<MepDevicePlacementProposalItem> placements)
    {
        return new MepDevicePlacementProposal(
            CoordinateSpace,
            LengthUnit,
            placements,
            state,
            reason,
            InstanceId,
            DocumentId,
            TemplateElementRef,
            SourceArtifactId,
            PageNumber,
            RasterWidth,
            RasterHeight);
    }

    private static Dictionary<string, object?> ToItem(string coordinateSpace, MepDevicePlacementProposalItem item)
    {
        var row = new Dictionary<string, object?>();
        if (coordinateSpace == MepDevicePlacementCoordinateSpaces.DocumentPage)
        {
            row["page_x"] = item.PageX;
            row["page_y"] = item.PageY;
        }
        else
        {
            row["offset_x"] = item.OffsetX;
            row["offset_y"] = item.OffsetY;
        }

        row["vertical_offset"] = item.VerticalOffset;
        if (item.SourceEvidence is not null)
        {
            row["source_evidence"] = item.SourceEvidence;
        }

        if (item.Confidence is not null)
        {
            row["confidence"] = item.Confidence;
        }

        return row;
    }
}

/// <summary>
/// Marker for the local project-axis confirmation. It is not a document calibration and not an approval.
/// </summary>
internal sealed class TrustedProjectAxisCalibration
{
    internal TrustedProjectAxisCalibration()
    {
    }
}

internal sealed class DocumentCalibrationSession
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    public DocumentCalibrationSession(
        string sessionRef,
        long generation,
        string artifactId,
        int pageNumber,
        int rasterWidth,
        int rasterHeight,
        string instanceId,
        string documentId,
        string templateElementRef,
        DateTimeOffset expiresAt)
    {
        SessionRef = sessionRef;
        Generation = generation;
        ArtifactId = artifactId;
        PageNumber = pageNumber;
        RasterWidth = rasterWidth;
        RasterHeight = rasterHeight;
        InstanceId = instanceId;
        DocumentId = documentId;
        TemplateElementRef = templateElementRef;
        ExpiresAt = expiresAt;
    }

    public string SessionRef { get; }

    public long Generation { get; }

    public string ArtifactId { get; }

    public int PageNumber { get; }

    public int RasterWidth { get; }

    public int RasterHeight { get; }

    public string InstanceId { get; }

    public string DocumentId { get; }

    public string TemplateElementRef { get; }

    public DateTimeOffset ExpiresAt { get; }

    public bool Matches(MepDevicePlacementProposal proposal, long generation)
    {
        return generation == Generation
            && string.Equals(proposal.CoordinateSpace, MepDevicePlacementCoordinateSpaces.DocumentPage, StringComparison.Ordinal)
            && string.Equals(proposal.InstanceId, InstanceId, StringComparison.Ordinal)
            && string.Equals(proposal.DocumentId, DocumentId, StringComparison.Ordinal)
            && string.Equals(proposal.TemplateElementRef, TemplateElementRef, StringComparison.Ordinal)
            && string.Equals(proposal.SourceArtifactId, ArtifactId, StringComparison.Ordinal)
            && proposal.PageNumber == PageNumber
            && proposal.RasterWidth == RasterWidth
            && proposal.RasterHeight == RasterHeight;
    }
}

/// <summary>
/// One active proposal for the current conversation. A later trusted calibration step can promote it without changing the model input contract.
/// </summary>
internal sealed class MepDevicePlacementPreviewReceipt
{
    public MepDevicePlacementPreviewReceipt(
        long generation,
        string intentRef,
        string? fingerprint,
        DateTimeOffset expiresAt,
        string summary)
    {
        Generation = generation;
        IntentRef = intentRef;
        Fingerprint = fingerprint;
        ExpiresAt = expiresAt;
        Summary = summary;
    }

    public long Generation { get; }

    public string IntentRef { get; }

    public string? Fingerprint { get; }

    public DateTimeOffset ExpiresAt { get; }

    public string Summary { get; }
}

internal sealed class MepDevicePlacementApplyReceipt
{
    public MepDevicePlacementApplyReceipt(long generation, string intentRef, string summary, bool isError)
    {
        Generation = generation;
        IntentRef = intentRef;
        Summary = summary;
        IsError = isError;
    }

    public long Generation { get; }

    public string IntentRef { get; }

    public string Summary { get; }

    public bool IsError { get; }
}

internal sealed class MepDevicePlacementProposalStore
{
    private readonly object _gate = new();
    private MepDevicePlacementProposal? _current;
    private long _generation;
    private MepDevicePlacementPreviewReceipt? _preview;
    private MepDevicePlacementApplyReceipt? _apply;
    private DocumentCalibrationSession? _calibration;

    public MepDevicePlacementProposal? Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public void Replace(MepDevicePlacementProposal proposal)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        lock (_gate)
        {
            _current = proposal;
            _generation++;
            _preview = null;
            _apply = null;
            _calibration = null;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _current = null;
            _generation++;
            _preview = null;
            _apply = null;
            _calibration = null;
        }
    }

    public void NoteAttachmentIdentityChanged(string? previousArtifactId, string? currentArtifactId)
    {
        lock (_gate)
        {
            var source = _current?.SourceArtifactId;
            if (string.IsNullOrEmpty(source))
            {
                return;
            }

            if (string.Equals(source, currentArtifactId, StringComparison.Ordinal))
            {
                return;
            }

            if (string.Equals(source, previousArtifactId, StringComparison.Ordinal))
            {
                _current = null;
                _generation++;
                _preview = null;
                _apply = null;
                _calibration = null;
            }
        }
    }

    public bool TryReusePreview(DateTimeOffset utcNow, out string summary)
    {
        lock (_gate)
        {
            if (_preview is null || _preview.Generation != _generation || _preview.ExpiresAt <= utcNow)
            {
                summary = "";
                return false;
            }

            summary = _preview.Summary;
            return true;
        }
    }

    public bool RememberPreview(string intentRef, string? fingerprint, DateTimeOffset expiresAt, string summary)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(intentRef);
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        lock (_gate)
        {
            if (_apply is not null && _apply.Generation == _generation)
            {
                return false;
            }

            _preview = new MepDevicePlacementPreviewReceipt(_generation, intentRef, fingerprint, expiresAt, summary);
            return true;
        }
    }

    public void ForgetPreview()
    {
        lock (_gate)
        {
            _preview = null;
        }
    }

    internal MepDevicePlacementPreviewReceipt? Preview
    {
        get
        {
            lock (_gate)
            {
                return _preview;
            }
        }
    }

    public bool TryGetUnexpiredPreview(DateTimeOffset utcNow, out MepDevicePlacementPreviewReceipt receipt)
    {
        lock (_gate)
        {
            if (_preview is null
                || _preview.Generation != _generation
                || _preview.ExpiresAt <= utcNow
                || string.IsNullOrWhiteSpace(_preview.IntentRef))
            {
                receipt = null!;
                return false;
            }

            receipt = _preview;
            return true;
        }
    }

    public bool TryReuseTerminalApply(out string summary, out bool isError)
    {
        lock (_gate)
        {
            if (_apply is null || _apply.Generation != _generation)
            {
                summary = "";
                isError = false;
                return false;
            }

            summary = _apply.Summary;
            isError = _apply.IsError;
            return true;
        }
    }

    public void RememberTerminalApply(string intentRef, string summary, bool isError)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(intentRef);
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        lock (_gate)
        {
            if (_preview is null
                || _preview.Generation != _generation
                || !string.Equals(_preview.IntentRef, intentRef, StringComparison.Ordinal))
            {
                return;
            }

            _apply = new MepDevicePlacementApplyReceipt(_generation, intentRef, summary, isError);
        }
    }

    internal MepDevicePlacementApplyReceipt? Apply
    {
        get
        {
            lock (_gate)
            {
                return _apply;
            }
        }
    }

    public bool TryPromoteToReady(TrustedProjectAxisCalibration calibration)
    {
        ArgumentNullException.ThrowIfNull(calibration);
        lock (_gate)
        {
            if (_current is not { HasTemplateBinding: true, CoordinateSpace: MepDevicePlacementCoordinateSpaces.ProjectOffsets } proposal)
            {
                return false;
            }

            if (_apply is not null && _apply.Generation == _generation)
            {
                return false;
            }

            _current = proposal.AsReady();
            return true;
        }
    }

    public DocumentCalibrationSession? OpenCalibrationSession(string sessionRef, DateTimeOffset expiresAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionRef);
        lock (_gate)
        {
            if (_current is not { HasTemplateBinding: true, CoordinateSpace: MepDevicePlacementCoordinateSpaces.DocumentPage } proposal
                || proposal.PageNumber is not int pageNumber
                || proposal.RasterWidth is not int rasterWidth
                || proposal.RasterHeight is not int rasterHeight
                || string.IsNullOrEmpty(proposal.SourceArtifactId)
                || string.IsNullOrEmpty(proposal.DocumentId)
                || string.IsNullOrEmpty(proposal.TemplateElementRef)
                || (_apply is not null && _apply.Generation == _generation))
            {
                _calibration = null;
                return null;
            }

            _calibration = new DocumentCalibrationSession(
                sessionRef,
                _generation,
                proposal.SourceArtifactId,
                pageNumber,
                rasterWidth,
                rasterHeight,
                proposal.InstanceId,
                proposal.DocumentId,
                proposal.TemplateElementRef,
                expiresAt);
            return _calibration;
        }
    }

    public bool TryReadCalibrationSession(string? sessionRef, DateTimeOffset utcNow, out DocumentCalibrationSession session)
    {
        lock (_gate)
        {
            if (_calibration is null
                || _current is null
                || !string.Equals(_calibration.SessionRef, sessionRef, StringComparison.Ordinal)
                || _calibration.ExpiresAt <= utcNow
                || !_calibration.Matches(_current, _generation))
            {
                session = null!;
                return false;
            }

            session = _calibration;
            return true;
        }
    }

    public void ForgetCalibrationSession()
    {
        lock (_gate)
        {
            _calibration = null;
        }
    }

    public bool TryApplyDocumentCalibration(
        DocumentCalibrationSession session,
        IReadOnlyList<MepDevicePlacementProposalItem> placements,
        DateTimeOffset utcNow)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(placements);
        lock (_gate)
        {
            if (_calibration is null
                || _current is null
                || !ReferenceEquals(_calibration, session) && !string.Equals(_calibration.SessionRef, session.SessionRef, StringComparison.Ordinal)
                || _calibration.ExpiresAt <= utcNow
                || !_calibration.Matches(_current, _generation)
                || !session.Matches(_current, _generation)
                || (_apply is not null && _apply.Generation == _generation)
                || placements.Count != _current.Placements.Count)
            {
                return false;
            }

            _current = _current.WithReadyOffsets(placements);
            _calibration = null;
            return true;
        }
    }

    internal DocumentCalibrationSession? Calibration
    {
        get
        {
            lock (_gate)
            {
                return _calibration;
            }
        }
    }
}
