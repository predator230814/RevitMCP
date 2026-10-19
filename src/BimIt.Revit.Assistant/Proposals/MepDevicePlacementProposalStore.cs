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
}

internal sealed record MepDevicePlacementProposalItem(
    double OffsetX,
    double OffsetY,
    double VerticalOffset,
    string? SourceEvidence,
    double? Confidence);

/// <summary>
/// One session-local proposal. Revit identities and the source attachment id stay on this object and are omitted from <see cref="ToModelResult"/>.
/// </summary>
internal sealed class MepDevicePlacementProposal
{
    private MepDevicePlacementProposal(
        string lengthUnit,
        IReadOnlyList<MepDevicePlacementProposalItem> placements,
        string state,
        string? reason,
        string instanceId,
        string? documentId,
        string? templateElementRef,
        string? sourceArtifactId)
    {
        LengthUnit = lengthUnit;
        Placements = placements;
        State = state;
        Reason = reason;
        InstanceId = instanceId;
        DocumentId = documentId;
        TemplateElementRef = templateElementRef;
        SourceArtifactId = sourceArtifactId;
    }

    public string LengthUnit { get; }

    public IReadOnlyList<MepDevicePlacementProposalItem> Placements { get; }

    public string State { get; }

    public string? Reason { get; }

    public string InstanceId { get; }

    public string? DocumentId { get; }

    public string? TemplateElementRef { get; }

    public string? SourceArtifactId { get; }

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
        string? sourceArtifactId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lengthUnit);
        ArgumentNullException.ThrowIfNull(placements);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        return new MepDevicePlacementProposal(
            lengthUnit,
            placements.ToArray(),
            MepDevicePlacementProposalStates.NeedsCalibration,
            reason,
            instanceId,
            string.IsNullOrEmpty(documentId) ? null : documentId,
            string.IsNullOrEmpty(templateElementRef) ? null : templateElementRef,
            string.IsNullOrEmpty(sourceArtifactId) ? null : sourceArtifactId);
    }

    public MepDevicePlacementProposal AsReady()
    {
        return new MepDevicePlacementProposal(
            LengthUnit,
            Placements,
            MepDevicePlacementProposalStates.Ready,
            null,
            InstanceId,
            DocumentId,
            TemplateElementRef,
            SourceArtifactId);
    }

    public Dictionary<string, object?> ToModelResult()
    {
        var proposal = new Dictionary<string, object?>
        {
            ["length_unit"] = LengthUnit,
            ["placements"] = Placements.Select(ToItem).ToArray(),
            ["state"] = State,
        };
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

    private static Dictionary<string, object?> ToItem(MepDevicePlacementProposalItem item)
    {
        var row = new Dictionary<string, object?>
        {
            ["offset_x"] = item.OffsetX,
            ["offset_y"] = item.OffsetY,
            ["vertical_offset"] = item.VerticalOffset,
        };
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
/// Marker for a later Assistant-owned calibration step. This slice has no provider that constructs it outside tests.
/// </summary>
internal sealed class TrustedProjectAxisCalibration
{
    internal TrustedProjectAxisCalibration()
    {
    }
}

/// <summary>
/// One active proposal for the current conversation. A later trusted calibration step can promote it without changing the model input contract.
/// </summary>
internal sealed class MepDevicePlacementProposalStore
{
    private readonly object _gate = new();
    private MepDevicePlacementProposal? _current;

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
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _current = null;
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
            }
        }
    }

    public bool TryPromoteToReady(TrustedProjectAxisCalibration calibration)
    {
        ArgumentNullException.ThrowIfNull(calibration);
        lock (_gate)
        {
            if (_current is not { HasTemplateBinding: true } proposal)
            {
                return false;
            }

            _current = proposal.AsReady();
            return true;
        }
    }
}
