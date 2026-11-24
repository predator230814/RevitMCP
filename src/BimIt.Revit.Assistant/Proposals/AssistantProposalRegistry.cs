namespace BimIt.Revit.Assistant;

/// <summary>
/// Conversation-level coordination for Assistant proposals. One registration is current.
/// Domain payloads, preview receipts, apply receipts, and Revit intent state stay in their stores.
/// </summary>
internal enum AssistantProposalKind
{
    MepDevicePlacement = 1,
    RoomPattern = 2,
}

internal static class AssistantProposalPresentation
{
    public const string NeedsCalibration = "needs_calibration";

    public const string Ready = "ready";

    public const string Proposed = "proposed";

    public static bool IsAllowed(string? presentation)
    {
        return presentation is NeedsCalibration or Ready or Proposed;
    }
}

/// <summary>
/// In-process generation for one registration. Generation 0 is unassigned.
/// This value is not model authority and is not an intent reference.
/// </summary>
internal readonly record struct AssistantProposalAuthority(AssistantProposalKind Kind, long Generation)
{
    public bool IsAssigned => Generation > 0;
}

internal sealed record AssistantProposalRegistration(
    AssistantProposalKind Kind,
    long Generation,
    string Presentation,
    string? SourceArtifactId);

internal sealed class AssistantProposalRegistry
{
    private readonly object _gate = new();
    private long _next;
    private AssistantProposalRegistration? _current;

    public AssistantProposalRegistration? Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public AssistantProposalAuthority Activate(AssistantProposalKind kind, string presentation, string? sourceArtifactId)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        if (!AssistantProposalPresentation.IsAllowed(presentation))
        {
            throw new ArgumentOutOfRangeException(nameof(presentation));
        }

        lock (_gate)
        {
            var generation = ++_next;
            _current = new AssistantProposalRegistration(
                kind,
                generation,
                presentation,
                string.IsNullOrEmpty(sourceArtifactId) ? null : sourceArtifactId);
            return new AssistantProposalAuthority(kind, generation);
        }
    }

    public bool IsCurrent(AssistantProposalAuthority authority)
    {
        if (authority.Generation <= 0)
        {
            return false;
        }

        lock (_gate)
        {
            return _current is not null
                && _current.Kind == authority.Kind
                && _current.Generation == authority.Generation;
        }
    }

    public bool TryUpdatePresentation(AssistantProposalAuthority authority, string presentation)
    {
        if (!AssistantProposalPresentation.IsAllowed(presentation))
        {
            throw new ArgumentOutOfRangeException(nameof(presentation));
        }

        lock (_gate)
        {
            if (_current is null
                || _current.Kind != authority.Kind
                || _current.Generation != authority.Generation)
            {
                return false;
            }

            _current = _current with { Presentation = presentation };
            return true;
        }
    }

    /// <summary>
    /// Drops the current registration when this authority still owns it.
    /// A release that does not own the current registration leaves the counter unchanged.
    /// </summary>
    public void Release(AssistantProposalAuthority authority)
    {
        if (authority.Generation <= 0)
        {
            return;
        }

        lock (_gate)
        {
            if (_current is null
                || _current.Kind != authority.Kind
                || _current.Generation != authority.Generation)
            {
                return;
            }

            _next++;
            _current = null;
        }
    }

    /// <summary>
    /// Drops the current registration when it depends on the attachment that was just replaced or removed.
    /// </summary>
    public void InvalidateCurrentIfSourceChanged(string? previousArtifactId, string? currentArtifactId)
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

            if (!string.Equals(source, previousArtifactId, StringComparison.Ordinal))
            {
                return;
            }

            _next++;
            _current = null;
        }
    }
}
