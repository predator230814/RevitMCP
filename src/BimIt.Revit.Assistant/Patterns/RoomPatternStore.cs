using BimIt.Revit.Contracts;
using BimIt.Revit.Assistant.Ipc;

namespace BimIt.Revit.Assistant.Patterns;

internal sealed class RoomPatternStore
{
    private readonly object _gate = new();
    private RoomPatternRecord? _current;
    private string? _reviewJson;
    private RoomPatternGeneration? _generation;
    private PatternLocalConfiguration? _configuration;
    private PatternPreviewBinding? _preview;
    private PatternTerminalApply? _terminal;
    private PatternTargetPending? _pending;

    public void Replace(RoomPatternRecord record, string? reviewJson, RoomPatternGeneration? generation = null)
    {
        ArgumentNullException.ThrowIfNull(record);
        lock (_gate)
        {
            _current = record;
            _reviewJson = reviewJson;
            _generation = generation;
            _configuration = generation is null ? null : PatternLocalConfiguration.Default(generation);
            _preview = null;
            _terminal = null;
            _pending = null;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _current = null;
            _reviewJson = null;
            _generation = null;
            _configuration = null;
            _preview = null;
            _terminal = null;
            _pending = null;
        }
    }

    public string? PeekReviewJson()
    {
        lock (_gate)
        {
            return _reviewJson;
        }
    }

    public RoomPatternGeneration? CurrentGeneration
    {
        get
        {
            lock (_gate)
            {
                return _generation;
            }
        }
    }

    public bool HasTerminalApply
    {
        get
        {
            lock (_gate)
            {
                return _terminal is not null && _generation is not null && _terminal.GenerationId == _generation.GenerationId;
            }
        }
    }

    public bool TryConfigure(PatternLocalConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        lock (_gate)
        {
            if (_generation is null || _terminal is not null || !configuration.Matches(_generation))
            {
                return false;
            }

            if (_configuration is not null && _configuration.Same(configuration))
            {
                return true;
            }

            _configuration = configuration;
            _preview = null;
            return true;
        }
    }

    /// <summary>
    /// Adds loaded types found by a Pattern Review search to the current generation. A type already offered keeps
    /// its key. Existing keys, the configuration, and a prepared preview stay valid.
    /// </summary>
    public bool TryAddOptions(
        string generationId,
        IReadOnlyList<LoadedMepTypeItem> items,
        out IReadOnlyList<LoadedMepTypeOption> added)
    {
        ArgumentNullException.ThrowIfNull(items);
        lock (_gate)
        {
            added = [];
            if (_generation is null || _terminal is not null || !string.Equals(_generation.GenerationId, generationId, StringComparison.Ordinal))
            {
                return false;
            }

            var options = new Dictionary<string, LoadedMepTypeOption>(_generation.Options, StringComparer.Ordinal);
            var byType = options.Values.ToDictionary(option => option.FamilyTypeRef, StringComparer.Ordinal);
            var next = options.Keys
                .Select(key => int.TryParse(key.AsSpan(1), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var index) ? index + 1 : 0)
                .DefaultIfEmpty(0)
                .Max();
            var found = new List<LoadedMepTypeOption>();
            foreach (var item in items)
            {
                if (!byType.TryGetValue(item.FamilyTypeRef, out var option))
                {
                    if (next > MaxOptionIndex)
                    {
                        break;
                    }

                    option = item.ToOption("t" + next.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    next++;
                    options[option.OptionKey] = option;
                    byType[option.FamilyTypeRef] = option;
                }

                if (!found.Contains(option))
                {
                    found.Add(option);
                }
            }

            _generation = new RoomPatternGeneration
            {
                GenerationId = _generation.GenerationId,
                InstanceId = _generation.InstanceId,
                DocumentId = _generation.DocumentId,
                ArtifactId = _generation.ArtifactId,
                PageNumber = _generation.PageNumber,
                Source = _generation.Source,
                Rooms = _generation.Rooms,
                Options = options,
            };
            added = found;
            return true;
        }
    }

    /// <summary>Option keys are <c>t0</c> to <c>t999</c> in the Pattern Review contract.</summary>
    public const int MaxOptionIndex = 999;

    public bool TryCurrentPreparation(out RoomPatternGeneration generation, out PatternLocalConfiguration configuration, out string failure)
    {
        lock (_gate)
        {
            generation = null!;
            configuration = null!;
            if (_generation is null || _configuration is null)
            {
                failure = "There is no pattern to prepare.";
                return false;
            }

            if (_terminal is not null)
            {
                failure = "This pattern already has a placement result. Create a new pattern before previewing another batch.";
                return false;
            }

            generation = _generation;
            configuration = _configuration;
            failure = "";
            return true;
        }
    }

    public void RememberPreview(string generationId, string intentRef, int placementCount)
    {
        lock (_gate)
        {
            if (_generation is null || !string.Equals(_generation.GenerationId, generationId, StringComparison.Ordinal))
            {
                return;
            }

            _preview = new PatternPreviewBinding(generationId, intentRef, placementCount);
        }
    }

    public bool TryPreview(out string intentRef, out int placementCount)
    {
        lock (_gate)
        {
            intentRef = "";
            placementCount = 0;
            if (_preview is null || _generation is null || _terminal is not null || _preview.GenerationId != _generation.GenerationId)
            {
                return false;
            }

            intentRef = _preview.IntentRef;
            placementCount = _preview.PlacementCount;
            return true;
        }
    }

    public void RememberTerminal(string generationId, string summary, bool isError)
    {
        lock (_gate)
        {
            if (_generation is null || !string.Equals(_generation.GenerationId, generationId, StringComparison.Ordinal))
            {
                return;
            }

            _terminal = new PatternTerminalApply(generationId, summary, isError);
            _preview = null;
        }
    }

    public void RememberPending(PatternTargetPending pending)
    {
        ArgumentNullException.ThrowIfNull(pending);
        lock (_gate)
        {
            if (!PendingIsCurrent(pending))
            {
                return;
            }

            _pending = pending;
        }
    }

    public bool TryCopyPending(out PatternTargetPending pending)
    {
        lock (_gate)
        {
            pending = null!;
            if (_pending is null || !PendingIsCurrent(_pending))
            {
                return false;
            }

            pending = _pending;
            return true;
        }
    }

    public bool PendingMatches(PatternTargetPending pending)
    {
        lock (_gate)
        {
            return _pending is not null
                && string.Equals(_pending.GenerationId, pending.GenerationId, StringComparison.Ordinal)
                && PendingIsCurrent(pending);
        }
    }

    public bool MarkTargetReasoningUnavailable(string generationId, string line)
    {
        lock (_gate)
        {
            if (_pending is null || !string.Equals(_pending.GenerationId, generationId, StringComparison.Ordinal) || !PendingIsCurrent(_pending))
            {
                return false;
            }

            var review = _pending.Snapshot.Review;
            review.Understood ??= [];
            if (review.Understood.Count < 12 && review.Understood.All(item => item != line))
            {
                review.Understood.Add(line);
            }

            var json = RoomPatternReview.Serialize(review);
            if (json is not null)
            {
                _reviewJson = json;
            }

            _pending = null;
            return true;
        }
    }

    public bool CompleteTargetReasoning(string generationId, PatternTargetOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        lock (_gate)
        {
            if (_pending is null
                || _generation is null
                || !string.Equals(_pending.GenerationId, generationId, StringComparison.Ordinal)
                || !string.Equals(_generation.GenerationId, generationId, StringComparison.Ordinal)
                || !PendingIsCurrent(_pending))
            {
                return false;
            }

            var json = RoomPatternReview.Serialize(outcome.Review);
            if (json is null)
            {
                _pending = null;
                return false;
            }

            _reviewJson = json;
            _generation = new RoomPatternGeneration
            {
                GenerationId = _generation.GenerationId,
                InstanceId = _generation.InstanceId,
                DocumentId = _generation.DocumentId,
                ArtifactId = _generation.ArtifactId,
                PageNumber = _generation.PageNumber,
                Source = _generation.Source,
                Options = _generation.Options,
                Rooms = outcome.Shown.Select((match, index) => new HiddenPatternRoom
                {
                    Index = index,
                    RoomRef = match.RoomRef ?? string.Empty,
                    Label = match.Label,
                    Band = match.Band,
                    Score = match.Score,
                    RotationDegrees = match.RotationDegrees,
                    Mirrored = match.Mirrored,
                    AdaptationStatus = match.AdaptationStatus,
                    Devices = match.Devices.Select(device => new HiddenPatternDevice
                    {
                        Role = device.Role,
                        FamilyHint = device.FamilyHint,
                        PlacementKind = device.PlacementKind,
                        XMetres = device.LocalX,
                        YMetres = device.LocalY,
                        QuarterTurns = device.PlacementKind == "interior" ? match.RotationDegrees / 90 : 0,
                    }).ToArray(),
                }).ToArray(),
            };
            _configuration = PatternLocalConfiguration.Default(_generation);
            _pending = null;
            return true;
        }
    }

    private bool PendingIsCurrent(PatternTargetPending pending)
    {
        return _generation is not null
            && _current is not null
            && string.Equals(_generation.GenerationId, pending.GenerationId, StringComparison.Ordinal)
            && string.Equals(_generation.InstanceId, pending.InstanceId, StringComparison.Ordinal)
            && string.Equals(_generation.DocumentId, pending.DocumentId, StringComparison.Ordinal)
            && string.Equals(_current.SourceArtifactId, pending.ArtifactId, StringComparison.Ordinal)
            && _current.PageNumber == pending.PageNumber;
    }

    public bool TryTerminal(out string summary, out bool isError)
    {
        lock (_gate)
        {
            summary = "";
            isError = false;
            if (_terminal is null || _generation is null || _terminal.GenerationId != _generation.GenerationId)
            {
                return false;
            }

            summary = _terminal.Summary;
            isError = _terminal.IsError;
            return true;
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
                ClearUnlocked();
            }
        }
    }

    private void ClearUnlocked()
    {
        _current = null;
        _reviewJson = null;
        _generation = null;
        _configuration = null;
        _preview = null;
        _terminal = null;
        _pending = null;
    }
}

internal sealed record RoomPatternRecord(string SourceArtifactId, int PageNumber, int DeviceCount);

internal sealed class RoomPatternGeneration
{
    public required string GenerationId { get; init; }

    public required string InstanceId { get; init; }

    public required string DocumentId { get; init; }

    public required string ArtifactId { get; init; }

    public required int PageNumber { get; init; }

    public required SourceRoomPattern Source { get; init; }

    public required IReadOnlyList<HiddenPatternRoom> Rooms { get; init; }

    public required IReadOnlyDictionary<string, LoadedMepTypeOption> Options { get; init; }
}

internal sealed class HiddenPatternRoom
{
    public required int Index { get; init; }

    public required string RoomRef { get; init; }

    public required string Label { get; init; }

    public required string Band { get; init; }

    public required double Score { get; init; }

    public required int RotationDegrees { get; init; }

    public required bool Mirrored { get; init; }

    public string? AdaptationStatus { get; init; }

    public required IReadOnlyList<HiddenPatternDevice> Devices { get; init; }
}

internal sealed class HiddenPatternDevice
{
    public required string Role { get; init; }

    public string? FamilyHint { get; init; }

    public required string PlacementKind { get; init; }

    public required double XMetres { get; init; }

    public required double YMetres { get; init; }

    public required int QuarterTurns { get; init; }
}

internal sealed class PatternLocalConfiguration
{
    // The person may leave a role out of the placement. It is never a type option key (t0–t999).
    public const string OmitOption = "omit";

    public required IReadOnlyList<int> SelectedRooms { get; init; }

    public required IReadOnlyDictionary<string, string> RoleOptions { get; init; }

    public required IReadOnlyDictionary<string, int> HeightMillimetres { get; init; }

    public static PatternLocalConfiguration Default(RoomPatternGeneration generation)
    {
        var selected = generation.Rooms
            .Where(room => room.Band == "high" && PatternAdaptationStatus.Selectable(room.AdaptationStatus))
            .Select(room => room.Index)
            .ToArray();
        return new PatternLocalConfiguration
        {
            SelectedRooms = selected,
            RoleOptions = new Dictionary<string, string>(StringComparer.Ordinal),
            HeightMillimetres = new Dictionary<string, int>(),
        };
    }

    public bool Matches(RoomPatternGeneration generation)
    {
        if (SelectedRooms.Count is < 1 or > 8 || SelectedRooms.Distinct().Count() != SelectedRooms.Count)
        {
            return false;
        }

        foreach (var index in SelectedRooms)
        {
            var room = generation.Rooms.FirstOrDefault(item => item.Index == index);
            if (room is null || room.Band == "rejected" || string.IsNullOrEmpty(room.RoomRef) || !PatternAdaptationStatus.Selectable(room.AdaptationStatus))
            {
                return false;
            }
        }

        var roles = SelectedRoles(generation, SelectedRooms);
        if (roles.Count == 0 || roles.Any(role => !RoleOptions.ContainsKey(role)))
        {
            return false;
        }

        var placed = roles.Where(role => !Omits(role)).ToArray();
        if (placed.Length == 0 || placed.Any(role => !HeightMillimetres.ContainsKey(role)))
        {
            return false;
        }

        foreach (var role in placed)
        {
            if (!generation.Options.ContainsKey(RoleOptions[role]))
            {
                return false;
            }

            if (HeightMillimetres[role] is < 0 or > 6000)
            {
                return false;
            }
        }

        if (LinkedBoundaryConflict(generation) || RaisedInteriorWorkPlane(generation))
        {
            return false;
        }

        var count = generation.Rooms
            .Where(room => SelectedRooms.Contains(room.Index))
            .Sum(room => room.Devices.Count(device => !Omits(device.Role)));
        return count is >= 1 and <= 80;
    }

    public bool Omits(string role)
    {
        return RoleOptions.TryGetValue(role, out var key) && key == OmitOption;
    }

    public bool LinkedBoundaryConflict(RoomPatternGeneration generation)
    {
        return generation.Rooms
            .Where(room => SelectedRooms.Contains(room.Index) && SpatialRoomRefCodec.IsLink(room.RoomRef))
            .SelectMany(room => room.Devices)
            .Where(device => device.PlacementKind == "boundary")
            .Any(device => RoleOptions.TryGetValue(device.Role, out var key)
                && generation.Options.TryGetValue(key, out var option)
                && !PatternTypeCompatibility.LinkedBoundary(option.PlacementClassification));
    }

    // A work-plane-based interior device is hosted by the Level plane and cannot be raised above it.
    public bool RaisedInteriorWorkPlane(RoomPatternGeneration generation)
    {
        return generation.Rooms
            .Where(room => SelectedRooms.Contains(room.Index))
            .SelectMany(room => room.Devices)
            .Where(device => device.PlacementKind == "interior")
            .Any(device => RoleOptions.TryGetValue(device.Role, out var key)
                && generation.Options.TryGetValue(key, out var option)
                && option.PlacementClassification == "work_plane_based"
                && HeightMillimetres.TryGetValue(device.Role, out var height)
                && height != 0);
    }

    public bool Same(PatternLocalConfiguration other)
    {
        return SelectedRooms.SequenceEqual(other.SelectedRooms)
            && RoleOptions.Count == other.RoleOptions.Count
            && RoleOptions.All(pair => other.RoleOptions.TryGetValue(pair.Key, out var value) && value == pair.Value)
            && HeightMillimetres.Count == other.HeightMillimetres.Count
            && HeightMillimetres.All(pair => other.HeightMillimetres.TryGetValue(pair.Key, out var value) && value == pair.Value);
    }

    public static HashSet<string> SelectedRoles(RoomPatternGeneration generation, IReadOnlyList<int>? selected = null)
    {
        var indexes = selected ?? generation.Rooms.Where(room => room.Band == "high").Select(room => room.Index).ToArray();
        return generation.Rooms
            .Where(room => indexes.Contains(room.Index))
            .SelectMany(room => room.Devices)
            .Select(device => device.Role)
            .ToHashSet(StringComparer.Ordinal);
    }
}

internal sealed record PatternPreviewBinding(string GenerationId, string IntentRef, int PlacementCount);

internal sealed record PatternTerminalApply(string GenerationId, string Summary, bool IsError);
