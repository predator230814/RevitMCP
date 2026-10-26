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
    public required IReadOnlyList<int> SelectedRooms { get; init; }

    public required IReadOnlyDictionary<string, string> RoleOptions { get; init; }

    public required IReadOnlyDictionary<string, int> HeightMillimetres { get; init; }

    public static PatternLocalConfiguration Default(RoomPatternGeneration generation)
    {
        var selected = generation.Rooms.Where(room => room.Band == "high").Select(room => room.Index).ToArray();
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
            if (room is null || room.Band == "rejected" || string.IsNullOrEmpty(room.RoomRef))
            {
                return false;
            }
        }

        var roles = SelectedRoles(generation);
        if (roles.Count == 0 || roles.Any(role => !RoleOptions.ContainsKey(role) || !HeightMillimetres.ContainsKey(role)))
        {
            return false;
        }

        foreach (var role in roles)
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

        var count = generation.Rooms.Where(room => SelectedRooms.Contains(room.Index)).Sum(room => room.Devices.Count);
        return count is >= 1 and <= 80;
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
