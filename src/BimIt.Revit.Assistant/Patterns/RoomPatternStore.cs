namespace BimIt.Revit.Assistant.Patterns;

internal sealed class RoomPatternStore
{
    private readonly object _gate = new();
    private RoomPatternRecord? _current;
    private string? _reviewJson;

    public void Replace(RoomPatternRecord record, string? reviewJson)
    {
        ArgumentNullException.ThrowIfNull(record);
        lock (_gate)
        {
            _current = record;
            _reviewJson = reviewJson;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _current = null;
            _reviewJson = null;
        }
    }

    public string? PeekReviewJson()
    {
        lock (_gate)
        {
            return _reviewJson;
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
                _reviewJson = null;
            }
        }
    }
}

internal sealed record RoomPatternRecord(
    string SourceArtifactId,
    int PageNumber,
    int DeviceCount);
