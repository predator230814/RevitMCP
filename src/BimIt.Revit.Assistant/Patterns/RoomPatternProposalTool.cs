using System.ComponentModel;
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

    public const string RoomName = "invalid_room_name";

    public const string Rule = "invalid_rule";
}

internal sealed class RoomPatternProposalRequest
{
    [JsonPropertyName("page_number")]
    public int? PageNumber { get; init; }

    [JsonPropertyName("boundary")]
    [Description("One room outline as 3 to 32 points. Every point is {\"page_x\":0..1,\"page_y\":0..1}. page_x runs left to right. page_y runs top to bottom.")]
    public IReadOnlyList<RoomPatternPointRequest>? Boundary { get; init; }

    [JsonPropertyName("entrance")]
    [Description("Optional door or entrance as {\"page_x\":0..1,\"page_y\":0..1}. page_x runs left to right. page_y runs top to bottom.")]
    public RoomPatternPointRequest? Entrance { get; init; }

    [JsonPropertyName("devices")]
    public IReadOnlyList<RoomPatternDeviceRequest>? Devices { get; init; }

    [JsonPropertyName("room_name")]
    [Description("Optional room word from the user, such as studio. BIM-IT lists rooms whose names contain it. Omit this when the user asks for rooms of the same shape. Do not send a Revit identity.")]
    public string? RoomName { get; init; }

    [JsonPropertyName("rules")]
    [Description("Optional relationships. One rule is one relationship. One paired_symmetric rule means one pair. Encode a relationship inferred from the image or explicitly requested by the user, even when the image does not prove a user-stated relationship. Use entrance, room_center, nearest_boundary, opposite_boundary, corner, or paired_symmetric. Optional basis is observed or user_stated. An entrance rule does not need a page entrance point. Omit rules that neither the image nor the user supports. Do not send coordinates or Revit identities.")]
    public IReadOnlyList<RoomPatternRuleRequest>? Rules { get; init; }

    [JsonPropertyName("anchors")]
    [Description("Optional visual reference objects you see, such as a bed. Each anchor is {label, page_x, page_y}. These are observations, not placement coordinates.")]
    public IReadOnlyList<RoomPatternAnchorRequest>? Anchors { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}

internal sealed class RoomPatternPointRequest
{
    [JsonPropertyName("page_x")]
    [Description("Normalized page X from 0 to 1, left to right. Do not send x.")]
    public double? PageX { get; init; }

    [JsonPropertyName("page_y")]
    [Description("Normalized page Y from 0 to 1, top to bottom. Do not send y.")]
    public double? PageY { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}

internal sealed class RoomPatternDeviceRequest
{
    [JsonPropertyName("role")]
    [Description("One of receptacle, switch, luminaire, smoke_detector, thermostat, data, other.")]
    public string? Role { get; init; }

    [JsonPropertyName("page_x")]
    [Description("Normalized page X from 0 to 1, left to right.")]
    public double? PageX { get; init; }

    [JsonPropertyName("page_y")]
    [Description("Normalized page Y from 0 to 1, top to bottom.")]
    public double? PageY { get; init; }

    [JsonPropertyName("placement_kind")]
    [Description("boundary when the device sits on a wall, otherwise interior.")]
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

internal sealed class RoomPatternRuleRequest
{
    [JsonPropertyName("role")]
    public string? Role { get; init; }

    [JsonPropertyName("anchor")]
    public string? Anchor { get; init; }

    [JsonPropertyName("offset_m")]
    public double? OffsetMetres { get; init; }

    [JsonPropertyName("group")]
    public string? Group { get; init; }

    [JsonPropertyName("confidence")]
    public double? Confidence { get; init; }

    [JsonPropertyName("basis")]
    [Description("Optional. user_stated when the user explicitly requested the relationship. observed when the image supports it. Omit it when unsure.")]
    public string? Basis { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}

internal sealed class RoomPatternAnchorRequest
{
    [JsonPropertyName("label")]
    public string? Label { get; init; }

    [JsonPropertyName("page_x")]
    public double? PageX { get; init; }

    [JsonPropertyName("page_y")]
    public double? PageY { get; init; }

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
        if (!TryValidate(arguments, out var pattern, out var pageNumber, out var roomName, out var failure))
        {
            return Fail(failure);
        }

        var attachment = CurrentAttachment();
        var page = attachment?.VisualPages.FirstOrDefault(item => item.PageNumber == pageNumber);
        if (attachment is null || page is null)
        {
            return Fail(RoomPatternFailures.Attachment);
        }

        var geometry = PageFrame.ForMatching(pattern, page.RasterWidth, page.RasterHeight);
        var grounding = await RoomPatternGrounding.ReadAsync(_session, _instanceId, pattern, geometry, roomName, cancellationToken).ConfigureAwait(false);
        grounding.Review.PageNumber = pageNumber;
        if (RoomPatternReviewRaster.TryCreate(page.Bytes, out var mime, out var image))
        {
            grounding.Review.ImageMime = mime;
            grounding.Review.ImageBase64 = image;
        }

        var catalog = await PatternTypeCatalog.LoadAsync(_session, _instanceId, grounding.DocumentId, cancellationToken).ConfigureAwait(false);
        var generation = PatternGenerationBuilder.Apply(grounding, pattern, catalog, _instanceId, attachment.ArtifactId, pageNumber);
        var reviewJson = RoomPatternReview.Serialize(grounding.Review);
        _store.Replace(
            new RoomPatternRecord(attachment.ArtifactId, pageNumber, pattern.Devices.Count),
            reviewJson,
            generation);
        var model = new Dictionary<string, object?>(grounding.Model)
        {
            ["page_number"] = pageNumber,
        };
        if (generation is not null && grounding.TargetDraft is PatternTargetDraft draft)
        {
            _store.RememberPending(new PatternTargetPending(
                generation.GenerationId,
                _instanceId,
                grounding.DocumentId,
                attachment.ArtifactId,
                pageNumber,
                draft.Request,
                draft.Snapshot));
            model["target_reasoning"] = "pending";
        }

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
        out string? roomName,
        out string failure)
    {
        pattern = null!;
        pageNumber = 0;
        roomName = null;
        failure = RoomPatternFailures.Shape;
        if (arguments.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var property in arguments.EnumerateObject())
        {
            if (property.Name is not ("page_number" or "boundary" or "entrance" or "devices" or "room_name" or "rules" or "anchors"))
            {
                return false;
            }
        }

        if (!TryRoomName(arguments, out roomName, out failure))
        {
            return false;
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

        var semantics = NormalizeSemantics(arguments, out var rules, out var anchors);
        pattern = new SourceRoomPattern(boundary, entrance, devices, rules, anchors, semantics);
        return true;
    }

    private static SemanticReport NormalizeSemantics(
        JsonElement arguments,
        out IReadOnlyList<PatternRule> rules,
        out IReadOnlyList<PatternAnchor> anchors)
    {
        var codes = new List<string>();
        var dropped = 0;
        rules = ReadRules(arguments, codes, ref dropped);
        anchors = ReadAnchors(arguments, codes, ref dropped);
        var status = rules.Count == 0
            ? SemanticReport.None
            : dropped == 0 ? SemanticReport.Complete : SemanticReport.Partial;
        return new SemanticReport(status, Math.Min(dropped, 20), codes.Distinct(StringComparer.Ordinal).Take(8).ToArray());
    }

    private static IReadOnlyList<PatternRule> ReadRules(JsonElement arguments, List<string> codes, ref int dropped)
    {
        if (!arguments.TryGetProperty("rules", out var property) || property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return [];
        }

        if (property.ValueKind != JsonValueKind.Array)
        {
            Drop(codes, ref dropped, SemanticWarningCodes.MalformedRule);
            return [];
        }

        var parsed = new List<PatternRule>();
        var seen = 0;
        var length = property.GetArrayLength();
        foreach (var item in property.EnumerateArray())
        {
            seen++;
            if (seen > MaxDevices)
            {
                dropped += length - MaxDevices;
                if (codes.Count < 8 && !codes.Contains(SemanticWarningCodes.MalformedRule, StringComparer.Ordinal))
                {
                    codes.Add(SemanticWarningCodes.MalformedRule);
                }

                break;
            }

            if (!TrySemanticRule(item, out var rule, out var code))
            {
                Drop(codes, ref dropped, code);
                continue;
            }

            parsed.Add(rule);
        }

        return parsed;
    }

    private static bool TrySemanticRule(JsonElement item, out PatternRule rule, out string code)
    {
        rule = null!;
        code = SemanticWarningCodes.MalformedRule;
        if (item.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var field in item.EnumerateObject())
        {
            if (field.Name is not ("role" or "anchor" or "offset_m" or "group" or "confidence" or "basis"))
            {
                return false;
            }
        }

        if (!item.TryGetProperty("role", out var roleProperty) || roleProperty.ValueKind != JsonValueKind.String || !IsRole(roleProperty.GetString()))
        {
            return false;
        }

        if (!item.TryGetProperty("anchor", out var anchorProperty) || anchorProperty.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var anchor = anchorProperty.GetString();
        if (!PatternAnchors.IsAnchor(anchor))
        {
            code = SemanticWarningCodes.UnknownAnchor;
            return false;
        }

        double offset = 0;
        if (item.TryGetProperty("offset_m", out var offsetProperty) && offsetProperty.ValueKind != JsonValueKind.Null)
        {
            if (offsetProperty.ValueKind != JsonValueKind.Number || !offsetProperty.TryGetDouble(out offset) || !double.IsFinite(offset) || offset is < 0 or > 6)
            {
                return false;
            }
        }

        string? group = null;
        if (item.TryGetProperty("group", out var groupProperty) && groupProperty.ValueKind != JsonValueKind.Null)
        {
            if (groupProperty.ValueKind != JsonValueKind.String || !IsHumanLabel(groupProperty.GetString()))
            {
                return false;
            }

            group = groupProperty.GetString();
        }

        double? confidence = null;
        if (item.TryGetProperty("confidence", out var confidenceProperty) && confidenceProperty.ValueKind != JsonValueKind.Null)
        {
            if (!TryUnit(confidenceProperty, out var value))
            {
                return false;
            }

            confidence = value;
        }

        string? basis = null;
        if (item.TryGetProperty("basis", out var basisProperty) && basisProperty.ValueKind == JsonValueKind.String && PatternRuleBasis.IsBasis(basisProperty.GetString()))
        {
            basis = basisProperty.GetString();
        }

        rule = new PatternRule(roleProperty.GetString()!, anchor!, offset, group, confidence, basis);
        return true;
    }

    private static IReadOnlyList<PatternAnchor> ReadAnchors(JsonElement arguments, List<string> codes, ref int dropped)
    {
        if (!arguments.TryGetProperty("anchors", out var property) || property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return [];
        }

        if (property.ValueKind != JsonValueKind.Array)
        {
            Drop(codes, ref dropped, SemanticWarningCodes.MalformedAnchor);
            return [];
        }

        var parsed = new List<PatternAnchor>();
        var seen = 0;
        foreach (var item in property.EnumerateArray())
        {
            seen++;
            if (seen > 8 || !TrySemanticAnchor(item, out var anchor))
            {
                Drop(codes, ref dropped, SemanticWarningCodes.MalformedAnchor);
                continue;
            }

            parsed.Add(anchor);
        }

        return parsed;
    }

    private static bool TrySemanticAnchor(JsonElement item, out PatternAnchor anchor)
    {
        anchor = null!;
        if (item.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var field in item.EnumerateObject())
        {
            if (field.Name is not ("label" or "page_x" or "page_y"))
            {
                return false;
            }
        }

        if (!item.TryGetProperty("label", out var labelProperty) || labelProperty.ValueKind != JsonValueKind.String || !IsHumanLabel(labelProperty.GetString()))
        {
            return false;
        }

        if (!item.TryGetProperty("page_x", out var xProperty) || !item.TryGetProperty("page_y", out var yProperty) || !TryUnit(xProperty, out var x) || !TryUnit(yProperty, out var y))
        {
            return false;
        }

        anchor = new PatternAnchor(labelProperty.GetString()!, x, y);
        return true;
    }

    private static bool IsHumanLabel(string? value)
    {
        return value is not null
            && value.Length is >= 1 and <= 40
            && !value.Contains("..", StringComparison.Ordinal)
            && !value.Contains('\\')
            && !value.Contains('/')
            && !value.Contains(':')
            && value.All(character => !char.IsControl(character) && (char.IsLetterOrDigit(character) || character is ' ' or '-' or '\'' or '_'));
    }

    private static void Drop(List<string> codes, ref int dropped, string code)
    {
        dropped++;
        if (codes.Count < 8 && !codes.Contains(code, StringComparer.Ordinal))
        {
            codes.Add(code);
        }
    }

    private static bool IsRole(string? role)
    {
        return role is "receptacle" or "switch" or "luminaire" or "smoke_detector" or "thermostat" or "data" or "other";
    }

    private static bool TryRoomName(JsonElement arguments, out string? roomName, out string failure)
    {
        roomName = null;
        failure = RoomPatternFailures.RoomName;
        if (!arguments.TryGetProperty("room_name", out var property) || property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            failure = "";
            return true;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var text = property.GetString()?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > 40 || text.Any(static character => !char.IsLetterOrDigit(character) && character is not (' ' or '-' or '\'')))
        {
            return false;
        }

        roomName = text;
        failure = "";
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
            if (property.Name is not ("page_x" or "page_y"))
            {
                return false;
            }
        }

        if (!item.TryGetProperty("page_x", out var xProperty)
            || !item.TryGetProperty("page_y", out var yProperty)
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
        return point is not null && (HasNonFinite(point.PageX) || HasNonFinite(point.PageY));
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
            ["reason"] = Reason(code),
        };
    }

    private static string Reason(string code)
    {
        return code switch
        {
            RoomPatternFailures.Page => "page_number must be an integer of 1 or more for the attached page.",
            RoomPatternFailures.Coordinate => "Every boundary and entrance point must be {\"page_x\":0..1,\"page_y\":0..1}. page_x runs left to right. page_y runs top to bottom. Do not send x or y.",
            RoomPatternFailures.Count => "devices must contain 1 to 20 items.",
            RoomPatternFailures.Role => "role must be receptacle, switch, luminaire, smoke_detector, thermostat, data, or other.",
            RoomPatternFailures.Placement => "placement_kind must be boundary or interior.",
            RoomPatternFailures.Hint => "family_hint must be 1 to 80 characters when present.",
            RoomPatternFailures.Evidence => "evidence must be at most 240 characters when present.",
            RoomPatternFailures.Confidence => "confidence must be a number from 0 to 1 when present.",
            RoomPatternFailures.NonFinite => "Coordinates and confidence must be finite numbers.",
            RoomPatternFailures.Attachment => "Attach one visual page before proposing a room pattern.",
            RoomPatternFailures.RoomName => "room_name must be 1 to 40 letters, digits, spaces, hyphens, or apostrophes, such as studio. Do not send a Revit identity.",
            RoomPatternFailures.Rule => "A semantic rule that cannot be read is dropped. The visual pattern is kept. One paired_symmetric rule means one pair.",
            _ => "Send page_number, boundary, optional entrance, devices, optional rules, and optional room_name. Every point uses page_x and page_y from 0 to 1.",
        };
    }
}
