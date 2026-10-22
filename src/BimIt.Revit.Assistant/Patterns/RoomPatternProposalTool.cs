using System.Text.Json;
using System.Text.Json.Serialization;
using BimIt.Revit.Assistant.Ipc;
using BimIt.Revit.Assistant.Patterns;

namespace BimIt.Revit.Assistant;

internal static class RoomPatternFailures
{
    public const string Shape = "invalid_shape";

    public const string Page = "invalid_page";

    public const string Count = "device_count";

    public const string Coordinate = "coordinate_out_of_range";

    public const string Role = "invalid_role";

    public const string Placement = "invalid_placement";

    public const string Confidence = "invalid_confidence";

    public const string Evidence = "evidence_length";

    public const string Hint = "family_hint_length";

    public const string NonFinite = "non_finite";

    public const string Attachment = "visual_page_required";
}

internal sealed class RoomPatternProposalRequest
{
    [JsonPropertyName("page_number")]
    public int? PageNumber { get; init; }

    [JsonPropertyName("boundary")]
    public IReadOnlyList<RoomPatternPointRequest>? Boundary { get; init; }

    [JsonPropertyName("entrance")]
    public RoomPatternPointRequest? Entrance { get; init; }

    [JsonPropertyName("devices")]
    public IReadOnlyList<RoomPatternDeviceRequest>? Devices { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}

internal sealed class RoomPatternPointRequest
{
    [JsonPropertyName("x")]
    public double? X { get; init; }

    [JsonPropertyName("y")]
    public double? Y { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}

internal sealed class RoomPatternDeviceRequest
{
    [JsonPropertyName("role")]
    public string? Role { get; init; }

    [JsonPropertyName("page_x")]
    public double? PageX { get; init; }

    [JsonPropertyName("page_y")]
    public double? PageY { get; init; }

    [JsonPropertyName("placement_kind")]
    public string? PlacementKind { get; init; }

    [JsonPropertyName("family_hint")]
    public string? FamilyHint { get; init; }

    [JsonPropertyName("evidence")]
    public string? Evidence { get; init; }

    [JsonPropertyName("confidence")]
    public double? Confidence { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}

internal sealed class RoomPatternProposalTool
{
    public const string Name = "bimit_propose_room_pattern";

    public const int MinDevices = 1;

    public const int MaxDevices = 20;

    public const int MinBoundary = 3;

    public const int MaxBoundary = 32;

    public const int MaxHint = 80;

    public const int MaxEvidence = 240;

    private static readonly JsonSerializerOptions RequestJson = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly HashSet<string> Roles = new(StringComparer.Ordinal)
    {
        "receptacle",
        "switch",
        "luminaire",
        "smoke_detector",
        "thermostat",
        "data",
        "other",
    };

    private readonly object _usedLock = new();
    private readonly List<string> _used = [];
    private readonly IAssistantToolSession _session;
    private readonly string _instanceId;
    private readonly RoomPatternStore _store;
    private readonly Func<AssistantDocumentArtifact?> _attachment;

    public RoomPatternProposalTool(
        IAssistantToolSession session,
        string instanceId,
        RoomPatternStore store,
        Func<AssistantDocumentArtifact?> attachment)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            throw new ArgumentException("An exact instance_id is required.", nameof(instanceId));
        }

        _instanceId = instanceId;
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _attachment = attachment ?? throw new ArgumentNullException(nameof(attachment));
    }

    public IReadOnlyList<string> ConsumeUsedTools()
    {
        lock (_usedLock)
        {
            var copy = _used.ToArray();
            _used.Clear();
            return copy;
        }
    }

    public Task<IReadOnlyDictionary<string, object?>> InvokeRequestAsync(
        RoomPatternProposalRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null || HasExtension(request))
        {
            RecordUse();
            return Task.FromResult<IReadOnlyDictionary<string, object?>>(Fail(RoomPatternFailures.Shape));
        }

        if (ContainsNonFinite(request))
        {
            RecordUse();
            return Task.FromResult<IReadOnlyDictionary<string, object?>>(Fail(RoomPatternFailures.NonFinite));
        }

        JsonElement json;
        try
        {
            json = JsonSerializer.SerializeToElement(request, RequestJson);
        }
        catch (ArgumentException)
        {
            RecordUse();
            return Task.FromResult<IReadOnlyDictionary<string, object?>>(Fail(RoomPatternFailures.NonFinite));
        }

        return InvokeAsync(json, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<string, object?>> InvokeAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        RecordUse();
        if (!TryValidate(arguments, out var pattern, out var pageNumber, out var failure))
        {
            return Fail(failure);
        }

        var attachment = CurrentAttachment();
        var page = attachment?.VisualPages.FirstOrDefault(item => item.PageNumber == pageNumber);
        if (attachment is null || page is null)
        {
            return Fail(RoomPatternFailures.Attachment);
        }

        var grounding = await RoomPatternGrounding.ReadAsync(_session, _instanceId, pattern, cancellationToken).ConfigureAwait(false);
        grounding.Review.PageNumber = pageNumber;
        if (RoomPatternReviewRaster.TryCreate(page.Bytes, out var mime, out var image))
        {
            grounding.Review.ImageMime = mime;
            grounding.Review.ImageBase64 = image;
        }

        var reviewJson = RoomPatternReview.Serialize(grounding.Review);
        _store.Replace(new RoomPatternRecord(attachment.ArtifactId, pageNumber, pattern.Devices.Count), reviewJson);
        var model = new Dictionary<string, object?>(grounding.Model)
        {
            ["page_number"] = pageNumber,
        };
        return model;
    }

    private AssistantDocumentArtifact? CurrentAttachment()
    {
        try
        {
            var artifact = _attachment();
            return artifact is { IsDisposed: false } ? artifact : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool TryValidate(
        JsonElement arguments,
        out SourceRoomPattern pattern,
        out int pageNumber,
        out string failure)
    {
        pattern = null!;
        pageNumber = 0;
        failure = RoomPatternFailures.Shape;
        if (arguments.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var property in arguments.EnumerateObject())
        {
            if (property.Name is not ("page_number" or "boundary" or "entrance" or "devices"))
            {
                return false;
            }
        }

        if (!arguments.TryGetProperty("page_number", out var pageProperty)
            || pageProperty.ValueKind != JsonValueKind.Number
            || !pageProperty.TryGetInt32(out pageNumber)
            || pageNumber < 1)
        {
            failure = RoomPatternFailures.Page;
            return false;
        }

        if (!arguments.TryGetProperty("boundary", out var boundaryProperty)
            || boundaryProperty.ValueKind != JsonValueKind.Array
            || boundaryProperty.GetArrayLength() is < MinBoundary or > MaxBoundary
            || !TryPoints(boundaryProperty, out var boundary))
        {
            failure = RoomPatternFailures.Coordinate;
            return false;
        }

        PlanPoint? entrance = null;
        if (arguments.TryGetProperty("entrance", out var entranceProperty) && entranceProperty.ValueKind != JsonValueKind.Null)
        {
            if (!TryPoint(entranceProperty, out var point))
            {
                failure = RoomPatternFailures.Coordinate;
                return false;
            }

            entrance = point;
        }

        if (!arguments.TryGetProperty("devices", out var devicesProperty) || devicesProperty.ValueKind != JsonValueKind.Array)
        {
            failure = RoomPatternFailures.Count;
            return false;
        }

        var count = devicesProperty.GetArrayLength();
        if (count is < MinDevices or > MaxDevices)
        {
            failure = RoomPatternFailures.Count;
            return false;
        }

        var devices = new List<SourceDevice>(count);
        foreach (var item in devicesProperty.EnumerateArray())
        {
            if (!TryDevice(item, out var device, out failure))
            {
                return false;
            }

            devices.Add(device);
        }

        pattern = new SourceRoomPattern(boundary, entrance, devices);
        return true;
    }

    private static bool TryPoints(JsonElement array, out IReadOnlyList<PlanPoint> points)
    {
        var parsed = new List<PlanPoint>();
        foreach (var item in array.EnumerateArray())
        {
            if (!TryPoint(item, out var point))
            {
                points = [];
                return false;
            }

            parsed.Add(point);
        }

        points = parsed;
        return true;
    }

    private static bool TryPoint(JsonElement item, out PlanPoint point)
    {
        point = default;
        if (item.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var property in item.EnumerateObject())
        {
            if (property.Name is not ("x" or "y"))
            {
                return false;
            }
        }

        if (!item.TryGetProperty("x", out var xProperty)
            || !item.TryGetProperty("y", out var yProperty)
            || !TryUnit(xProperty, out var x)
            || !TryUnit(yProperty, out var y))
        {
            return false;
        }

        point = new PlanPoint(x, y);
        return true;
    }

    private static bool TryDevice(JsonElement item, out SourceDevice device, out string failure)
    {
        device = null!;
        failure = RoomPatternFailures.Shape;
        if (item.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var property in item.EnumerateObject())
        {
            if (property.Name is not ("role" or "page_x" or "page_y" or "placement_kind" or "family_hint" or "evidence" or "confidence"))
            {
                return false;
            }
        }

        if (!item.TryGetProperty("role", out var roleProperty)
            || roleProperty.ValueKind != JsonValueKind.String
            || roleProperty.GetString() is not string role
            || !Roles.Contains(role))
        {
            failure = RoomPatternFailures.Role;
            return false;
        }

        if (!item.TryGetProperty("page_x", out var xProperty)
            || !item.TryGetProperty("page_y", out var yProperty)
            || !TryUnit(xProperty, out var x)
            || !TryUnit(yProperty, out var y))
        {
            failure = RoomPatternFailures.Coordinate;
            return false;
        }

        if (!item.TryGetProperty("placement_kind", out var placementProperty)
            || placementProperty.ValueKind != JsonValueKind.String
            || placementProperty.GetString() is not ("boundary" or "interior"))
        {
            failure = RoomPatternFailures.Placement;
            return false;
        }

        var placement = placementProperty.GetString()!;
        string? hint = null;
        if (item.TryGetProperty("family_hint", out var hintProperty) && hintProperty.ValueKind != JsonValueKind.Null)
        {
            if (hintProperty.ValueKind != JsonValueKind.String || hintProperty.GetString() is not string hintText || hintText.Length is < 1 or > MaxHint)
            {
                failure = RoomPatternFailures.Hint;
                return false;
            }

            hint = hintText;
        }

        if (item.TryGetProperty("evidence", out var evidenceProperty) && evidenceProperty.ValueKind != JsonValueKind.Null)
        {
            if (evidenceProperty.ValueKind != JsonValueKind.String || evidenceProperty.GetString() is not string evidence || evidence.Length > MaxEvidence)
            {
                failure = RoomPatternFailures.Evidence;
                return false;
            }
        }

        if (item.TryGetProperty("confidence", out var confidenceProperty) && confidenceProperty.ValueKind != JsonValueKind.Null)
        {
            if (!confidenceProperty.TryGetDouble(out var confidence) || confidence is < 0 or > 1 || !double.IsFinite(confidence))
            {
                failure = RoomPatternFailures.Confidence;
                return false;
            }
        }

        device = new SourceDevice(role, x, y, placement, hint);
        return true;
    }

    private static bool TryUnit(JsonElement property, out double value)
    {
        value = 0;
        return property.ValueKind == JsonValueKind.Number
            && property.TryGetDouble(out value)
            && double.IsFinite(value)
            && value is >= 0 and <= 1;
    }

    private static bool ContainsNonFinite(RoomPatternProposalRequest request)
    {
        if (HasNonFinite(request.Entrance) || (request.Boundary?.Any(HasNonFinite) ?? false))
        {
            return true;
        }

        return request.Devices?.Any(device => device is not null && (HasNonFinite(device.PageX) || HasNonFinite(device.PageY) || HasNonFinite(device.Confidence))) ?? false;
    }

    private static bool HasNonFinite(RoomPatternPointRequest? point)
    {
        return point is not null && (HasNonFinite(point.X) || HasNonFinite(point.Y));
    }

    private static bool HasNonFinite(double? value) => value is double number && !double.IsFinite(number);

    private static bool HasExtension(RoomPatternProposalRequest request)
    {
        return request.ExtensionData is { Count: > 0 }
            || request.Entrance?.ExtensionData is { Count: > 0 }
            || (request.Boundary?.Any(point => point?.ExtensionData is { Count: > 0 }) ?? false)
            || (request.Devices?.Any(device => device?.ExtensionData is { Count: > 0 }) ?? false);
    }

    private void RecordUse()
    {
        lock (_usedLock)
        {
            if (_used.Count < AssistantIpc.MaxUsedToolCount)
            {
                _used.Add(Name);
            }
        }
    }

    private static Dictionary<string, object?> Fail(string code)
    {
        return new Dictionary<string, object?>
        {
            ["stored"] = false,
            ["code"] = code,
        };
    }
}
