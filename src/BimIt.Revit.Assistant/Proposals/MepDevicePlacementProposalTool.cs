using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using BimIt.Revit.Assistant.Ipc;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Assistant;

internal static class MepDevicePlacementProposalFailures
{
    public const string Code = "invalid_proposal";

    public const string Shape = "invalid_shape";

    public const string Count = "placement_count";

    public const string NonFinite = "non_finite";

    public const string Unit = "unsupported_length_unit";

    public const string Range = "offset_out_of_range";

    public const string Duplicate = "duplicate_offset";

    public const string Confidence = "invalid_confidence";

    public const string Evidence = "evidence_length";

    public const string CoordinateSpace = "coordinate_space";

    public const string Mixed = "mixed_coordinates";

    public const string Page = "page_unavailable";
}

internal sealed class MepDevicePlacementProposalRequest
{
    [JsonPropertyName("coordinate_space")]
    [Description("project_offsets or document_page.")]
    public string? CoordinateSpace { get; init; }

    [JsonPropertyName("length_unit")]
    [Description("Length unit for every offset. Use mm, cm, m, in, or ft.")]
    public string? LengthUnit { get; init; }

    [JsonPropertyName("page_number")]
    [Description("1-based drawing page. Required for document_page.")]
    public int? PageNumber { get; init; }

    [JsonPropertyName("placements")]
    [Description("One to twenty placements.")]
    public IReadOnlyList<MepDevicePlacementProposalItemRequest>? Placements { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}

internal sealed class MepDevicePlacementProposalItemRequest
{
    [JsonPropertyName("offset_x")]
    [Description("Signed project-axis X offset in the proposal length unit. Use only with project_offsets.")]
    public double? OffsetX { get; init; }

    [JsonPropertyName("offset_y")]
    [Description("Signed project-axis Y offset in the proposal length unit. Use only with project_offsets.")]
    public double? OffsetY { get; init; }

    [JsonPropertyName("page_x")]
    [Description("Normalized drawing position from 0 to 1, left to right. Use only with document_page.")]
    public double? PageX { get; init; }

    [JsonPropertyName("page_y")]
    [Description("Normalized drawing position from 0 to 1, top to bottom. Use only with document_page.")]
    public double? PageY { get; init; }

    [JsonPropertyName("vertical_offset")]
    [Description("Signed vertical offset in the proposal length unit.")]
    public double? VerticalOffset { get; init; }

    [JsonPropertyName("source_evidence")]
    [Description("Optional informational note, at most 240 characters.")]
    public string? SourceEvidence { get; init; }

    [JsonPropertyName("confidence")]
    [Description("Optional informational confidence from 0 to 1.")]
    public double? Confidence { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}

internal sealed class MepDevicePlacementProposalTool
{
    public const string Name = "bimit_propose_mep_device_placements";

    public const int MinPlacements = 1;

    public const int MaxPlacements = 20;

    public const int SelectionProbeLimit = 2;

    public const int MaxEvidenceCharacters = 240;

    public const int MaxEvidenceRawCharacters = 4_096;

    public const double MaxPlanMetres = 100;

    public const double MaxVerticalMetres = 10;

    private const double BoundToleranceMetres = 1e-9;

    private static readonly JsonSerializerOptions RequestJson = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly HashSet<string> ProjectFields = new(StringComparer.Ordinal)
    {
        "coordinate_space",
        "length_unit",
        "placements",
    };

    private static readonly HashSet<string> DocumentFields = new(StringComparer.Ordinal)
    {
        "coordinate_space",
        "length_unit",
        "page_number",
        "placements",
    };

    private static readonly HashSet<string> ProjectPlacementFields = new(StringComparer.Ordinal)
    {
        "offset_x",
        "offset_y",
        "vertical_offset",
        "source_evidence",
        "confidence",
    };

    private static readonly HashSet<string> DocumentPlacementFields = new(StringComparer.Ordinal)
    {
        "page_x",
        "page_y",
        "vertical_offset",
        "source_evidence",
        "confidence",
    };

    private readonly object _usedLock = new();
    private readonly List<string> _used = [];
    private readonly IAssistantToolSession _session;
    private readonly string _instanceId;
    private readonly MepDevicePlacementProposalStore _store;
    private readonly Func<AssistantAttachmentView> _currentAttachment;

    public MepDevicePlacementProposalTool(
        IAssistantToolSession session,
        string instanceId,
        MepDevicePlacementProposalStore store,
        Func<AssistantAttachmentView> currentAttachment)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            throw new ArgumentException("An exact instance_id is required.", nameof(instanceId));
        }

        _instanceId = instanceId;
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _currentAttachment = currentAttachment ?? throw new ArgumentNullException(nameof(currentAttachment));
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
        MepDevicePlacementProposalRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            RecordUse();
            return Task.FromResult<IReadOnlyDictionary<string, object?>>(Fail(MepDevicePlacementProposalFailures.Shape));
        }

        if (ContainsNonFinite(request))
        {
            RecordUse();
            return Task.FromResult<IReadOnlyDictionary<string, object?>>(Fail(MepDevicePlacementProposalFailures.NonFinite));
        }

        JsonElement json;
        try
        {
            json = JsonSerializer.SerializeToElement(request, RequestJson);
        }
        catch (ArgumentException)
        {
            RecordUse();
            return Task.FromResult<IReadOnlyDictionary<string, object?>>(Fail(MepDevicePlacementProposalFailures.NonFinite));
        }

        return InvokeAsync(json, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<string, object?>> InvokeAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        RecordUse();
        var attachment = CurrentAttachment();
        if (!TryValidate(arguments, attachment, out var coordinateSpace, out var lengthUnit, out var pageNumber, out var rasterWidth, out var rasterHeight, out var placements, out var failure))
        {
            return Fail(failure);
        }

        var binding = await ResolveBindingAsync(cancellationToken).ConfigureAwait(false);
        var reason = coordinateSpace == MepDevicePlacementCoordinateSpaces.DocumentPage
            && binding.Reason == MepDevicePlacementProposalReasons.CoordinateBasisUnestablished
            ? MepDevicePlacementProposalReasons.DocumentCalibrationRequired
            : binding.Reason;
        var proposal = MepDevicePlacementProposal.Create(
            lengthUnit,
            placements,
            reason,
            _instanceId,
            binding.DocumentId,
            binding.TemplateElementRef,
            attachment.ArtifactId,
            coordinateSpace,
            pageNumber,
            rasterWidth,
            rasterHeight);
        _store.Replace(proposal);
        return proposal.ToModelResult();
    }

    private static bool ContainsNonFinite(MepDevicePlacementProposalRequest request)
    {
        if (request.Placements is null)
        {
            return false;
        }

        foreach (var item in request.Placements)
        {
            if (item is null)
            {
                continue;
            }

            if (IsPresentAndNonFinite(item.OffsetX)
                || IsPresentAndNonFinite(item.OffsetY)
                || IsPresentAndNonFinite(item.PageX)
                || IsPresentAndNonFinite(item.PageY)
                || IsPresentAndNonFinite(item.VerticalOffset)
                || IsPresentAndNonFinite(item.Confidence))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsPresentAndNonFinite(double? value)
    {
        return value is double number && !double.IsFinite(number);
    }

    private AssistantAttachmentView CurrentAttachment()
    {
        try
        {
            return _currentAttachment() ?? AssistantAttachmentView.None;
        }
        catch (Exception)
        {
            return AssistantAttachmentView.None;
        }
    }

    private async Task<SelectionBinding> ResolveBindingAsync(CancellationToken cancellationToken)
    {
        AssistantToolCall call;
        try
        {
            call = await _session.CallAsync(
                AssistantTools.GetSelection,
                new Dictionary<string, object?>
                {
                    ["instance_id"] = _instanceId,
                    ["limit"] = SelectionProbeLimit,
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return SelectionBinding.Unresolved(MepDevicePlacementProposalReasons.TemplateSelectionRequired);
        }

        if (call.IsError)
        {
            var reason = string.Equals(call.ErrorCode, CapabilityErrorCodes.NoActiveDocument, StringComparison.Ordinal)
                ? MepDevicePlacementProposalReasons.NoActiveDocument
                : MepDevicePlacementProposalReasons.TemplateSelectionRequired;
            return SelectionBinding.Unresolved(reason);
        }

        if (!TryReadSelection(call.Structured, out var documentId, out var selectedCount, out var truncated, out var refs))
        {
            return SelectionBinding.Unresolved(MepDevicePlacementProposalReasons.TemplateSelectionRequired);
        }

        if (string.IsNullOrEmpty(documentId))
        {
            return SelectionBinding.Unresolved(MepDevicePlacementProposalReasons.NoActiveDocument);
        }

        if (selectedCount == 1 && !truncated && refs.Count == 1 && !string.IsNullOrEmpty(refs[0]))
        {
            return new SelectionBinding(MepDevicePlacementProposalReasons.CoordinateBasisUnestablished, documentId, refs[0]);
        }

        return SelectionBinding.Unresolved(MepDevicePlacementProposalReasons.TemplateSelectionRequired);
    }

    private static bool TryValidate(
        JsonElement arguments,
        AssistantAttachmentView attachment,
        out string coordinateSpace,
        out string lengthUnit,
        out int? pageNumber,
        out int? rasterWidth,
        out int? rasterHeight,
        out List<MepDevicePlacementProposalItem> placements,
        out string failure)
    {
        coordinateSpace = "";
        lengthUnit = "";
        pageNumber = null;
        rasterWidth = null;
        rasterHeight = null;
        placements = [];
        failure = MepDevicePlacementProposalFailures.Shape;
        if (arguments.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!TryCoordinateSpace(arguments, out coordinateSpace, out failure))
        {
            return false;
        }

        var documentPage = coordinateSpace == MepDevicePlacementCoordinateSpaces.DocumentPage;
        if (HasMixed(arguments, documentPage))
        {
            failure = MepDevicePlacementProposalFailures.Mixed;
            return false;
        }

        if (HasUnknown(arguments, documentPage ? DocumentFields : ProjectFields))
        {
            failure = MepDevicePlacementProposalFailures.Shape;
            return false;
        }

        if (!arguments.TryGetProperty("length_unit", out var unitProperty))
        {
            failure = MepDevicePlacementProposalFailures.Unit;
            return false;
        }

        if (!TryUnit(unitProperty, out lengthUnit, out failure))
        {
            return false;
        }

        if (documentPage && !TryDocumentPage(arguments, attachment, out pageNumber, out rasterWidth, out rasterHeight, out failure))
        {
            return false;
        }

        if (!arguments.TryGetProperty("placements", out var placementProperty) || placementProperty.ValueKind != JsonValueKind.Array)
        {
            failure = MepDevicePlacementProposalFailures.Shape;
            return false;
        }

        var count = placementProperty.GetArrayLength();
        if (count is < MinPlacements or > MaxPlacements)
        {
            failure = MepDevicePlacementProposalFailures.Count;
            return false;
        }

        var seen = new HashSet<OffsetTriple>();
        foreach (var item in placementProperty.EnumerateArray())
        {
            if (!TryPlacement(item, lengthUnit, documentPage, out var placement, out failure))
            {
                placements = [];
                return false;
            }

            var key = documentPage
                ? new OffsetTriple(placement.PageX!.Value, placement.PageY!.Value, placement.VerticalOffset)
                : new OffsetTriple(placement.OffsetX, placement.OffsetY, placement.VerticalOffset);
            if (!seen.Add(key))
            {
                failure = MepDevicePlacementProposalFailures.Duplicate;
                placements = [];
                return false;
            }

            placements.Add(placement);
        }

        return true;
    }

    private static bool TryCoordinateSpace(JsonElement arguments, out string coordinateSpace, out string failure)
    {
        coordinateSpace = "";
        failure = MepDevicePlacementProposalFailures.CoordinateSpace;
        if (!arguments.TryGetProperty("coordinate_space", out var property))
        {
            return false;
        }

        if (property.ValueKind != JsonValueKind.String || property.GetString() is not string text)
        {
            failure = MepDevicePlacementProposalFailures.Shape;
            return false;
        }

        coordinateSpace = text;
        return coordinateSpace is MepDevicePlacementCoordinateSpaces.ProjectOffsets or MepDevicePlacementCoordinateSpaces.DocumentPage;
    }

    private static bool HasMixed(JsonElement arguments, bool documentPage)
    {
        if (!documentPage && arguments.TryGetProperty("page_number", out _))
        {
            return true;
        }

        if (!arguments.TryGetProperty("placements", out var placements) || placements.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var item in placements.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var hasProject = item.TryGetProperty("offset_x", out _) || item.TryGetProperty("offset_y", out _);
            var hasPage = item.TryGetProperty("page_x", out _) || item.TryGetProperty("page_y", out _);
            if (documentPage ? hasProject : hasPage)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryDocumentPage(
        JsonElement arguments,
        AssistantAttachmentView attachment,
        out int? pageNumber,
        out int? rasterWidth,
        out int? rasterHeight,
        out string failure)
    {
        pageNumber = null;
        rasterWidth = null;
        rasterHeight = null;
        failure = MepDevicePlacementProposalFailures.Shape;
        if (!arguments.TryGetProperty("page_number", out var property))
        {
            return false;
        }

        if (property.ValueKind != JsonValueKind.Number || !property.TryGetInt32(out var number))
        {
            return false;
        }

        failure = MepDevicePlacementProposalFailures.Page;
        if (number < 1 || string.IsNullOrEmpty(attachment.ArtifactId))
        {
            return false;
        }

        foreach (var page in attachment.Pages)
        {
            if (page.PageNumber == number && page.RasterWidth > 0 && page.RasterHeight > 0)
            {
                pageNumber = number;
                rasterWidth = page.RasterWidth;
                rasterHeight = page.RasterHeight;
                return true;
            }
        }

        return false;
    }

    private static bool TryUnit(JsonElement unit, out string canonical, out string failure)
    {
        canonical = "";
        failure = MepDevicePlacementProposalFailures.Unit;
        if (unit.ValueKind != JsonValueKind.String || unit.GetString() is not string text)
        {
            failure = MepDevicePlacementProposalFailures.Shape;
            return false;
        }

        canonical = text.Trim();
        return canonical is "mm" or "cm" or "m" or "in" or "ft";
    }

    private static bool TryPlacement(
        JsonElement item,
        string lengthUnit,
        bool documentPage,
        out MepDevicePlacementProposalItem placement,
        out string failure)
    {
        placement = new MepDevicePlacementProposalItem(0, 0, 0, null, null);
        failure = MepDevicePlacementProposalFailures.Shape;
        if (item.ValueKind != JsonValueKind.Object || HasUnknown(item, documentPage ? DocumentPlacementFields : ProjectPlacementFields))
        {
            return false;
        }

        if (!TryOffset(item, "vertical_offset", out var vertical, out failure))
        {
            return false;
        }

        double offsetX = 0;
        double offsetY = 0;
        double? pageX = null;
        double? pageY = null;
        if (documentPage)
        {
            if (!TryUnitCoordinate(item, "page_x", out var x, out failure) || !TryUnitCoordinate(item, "page_y", out var y, out failure))
            {
                return false;
            }

            pageX = x;
            pageY = y;
        }
        else if (!TryOffset(item, "offset_x", out offsetX, out failure) || !TryOffset(item, "offset_y", out offsetY, out failure))
        {
            return false;
        }

        if (!TryEvidence(item, out var evidence, out failure) || !TryConfidence(item, out var confidence, out failure))
        {
            return false;
        }

        if (!WithinPlan(lengthUnit, documentPage, offsetX, offsetY, vertical))
        {
            failure = PlanIsFinite(lengthUnit, documentPage, offsetX, offsetY, vertical)
                ? MepDevicePlacementProposalFailures.Range
                : MepDevicePlacementProposalFailures.NonFinite;
            return false;
        }

        placement = new MepDevicePlacementProposalItem(offsetX, offsetY, vertical, evidence, confidence, pageX, pageY);
        return true;
    }

    private static bool TryUnitCoordinate(JsonElement item, string name, out double value, out string failure)
    {
        if (!TryOffset(item, name, out value, out failure))
        {
            return false;
        }

        if (!DocumentPageSimilarity.IsUnitInterval(value))
        {
            failure = MepDevicePlacementProposalFailures.Range;
            return false;
        }

        return true;
    }

    private static bool TryOffset(JsonElement item, string name, out double value, out string failure)
    {
        value = 0;
        failure = MepDevicePlacementProposalFailures.Shape;
        if (!item.TryGetProperty(name, out var property))
        {
            return false;
        }

        if (property.ValueKind != JsonValueKind.Number)
        {
            return false;
        }

        if (!property.TryGetDouble(out value) || !double.IsFinite(value))
        {
            failure = MepDevicePlacementProposalFailures.NonFinite;
            return false;
        }

        return true;
    }

    private static bool TryEvidence(JsonElement item, out string? evidence, out string failure)
    {
        evidence = null;
        failure = MepDevicePlacementProposalFailures.Shape;
        if (!item.TryGetProperty("source_evidence", out var property) || property.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (property.ValueKind != JsonValueKind.String || property.GetString() is not string text)
        {
            return false;
        }

        if (text.Length > MaxEvidenceRawCharacters)
        {
            failure = MepDevicePlacementProposalFailures.Evidence;
            return false;
        }

        var trimmed = text.Trim();
        if (trimmed.Length > MaxEvidenceCharacters)
        {
            failure = MepDevicePlacementProposalFailures.Evidence;
            return false;
        }

        evidence = trimmed.Length == 0 ? null : trimmed;
        return true;
    }

    private static bool TryConfidence(JsonElement item, out double? confidence, out string failure)
    {
        confidence = null;
        failure = MepDevicePlacementProposalFailures.Shape;
        if (!item.TryGetProperty("confidence", out var property) || property.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (property.ValueKind != JsonValueKind.Number)
        {
            return false;
        }

        if (!property.TryGetDouble(out var value) || !double.IsFinite(value))
        {
            failure = MepDevicePlacementProposalFailures.Confidence;
            return false;
        }

        if (value < 0 || value > 1)
        {
            failure = MepDevicePlacementProposalFailures.Confidence;
            return false;
        }

        confidence = value;
        return true;
    }

    private static bool WithinPlan(string unit, bool documentPage, double offsetX, double offsetY, double vertical)
    {
        if (!MepDevicePlacementLengthUnits.TryToMetres(unit, vertical, out var z))
        {
            return false;
        }

        if (Math.Abs(z) > MaxVerticalMetres + BoundToleranceMetres)
        {
            return false;
        }

        if (documentPage)
        {
            return true;
        }

        return MepDevicePlacementLengthUnits.TryToMetres(unit, offsetX, out var x)
            && MepDevicePlacementLengthUnits.TryToMetres(unit, offsetY, out var y)
            && Math.Abs(x) <= MaxPlanMetres + BoundToleranceMetres
            && Math.Abs(y) <= MaxPlanMetres + BoundToleranceMetres;
    }

    private static bool PlanIsFinite(string unit, bool documentPage, double offsetX, double offsetY, double vertical)
    {
        if (!MepDevicePlacementLengthUnits.TryToMetres(unit, vertical, out _))
        {
            return false;
        }

        return documentPage
            || (MepDevicePlacementLengthUnits.TryToMetres(unit, offsetX, out _)
                && MepDevicePlacementLengthUnits.TryToMetres(unit, offsetY, out _));
    }

    private static bool HasUnknown(JsonElement item, HashSet<string> allowed)
    {
        foreach (var property in item.EnumerateObject())
        {
            if (!allowed.Contains(property.Name))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryReadSelection(
        JsonElement? structured,
        out string documentId,
        out int selectedCount,
        out bool truncated,
        out List<string> refs)
    {
        return MepDevicePlacementSelectionProbe.TryRead(structured, out documentId, out selectedCount, out truncated, out refs);
    }

    private void RecordUse()
    {
        lock (_usedLock)
        {
            if (_used.Count >= AssistantIpc.MaxUsedToolCount)
            {
                return;
            }

            _used.Add(Name);
        }
    }

    private static Dictionary<string, object?> Fail(string reason)
    {
        return new Dictionary<string, object?>
        {
            ["ok"] = false,
            ["code"] = MepDevicePlacementProposalFailures.Code,
            ["reason"] = reason,
            ["message"] = "The placement proposal is invalid.",
        };
    }

    private readonly record struct OffsetTriple(double X, double Y, double Z);

    private readonly record struct SelectionBinding(string Reason, string? DocumentId, string? TemplateElementRef)
    {
        public static SelectionBinding Unresolved(string reason) => new(reason, null, null);
    }
}

internal static class AssistantModelTools
{
    public static readonly string[] Names =
    [
        BimItAgentReadTools.ModelContext,
        BimItAgentReadTools.Warnings,
        BimItAgentReadTools.ActiveViewElements,
        BimItAgentReadTools.InspectSelection,
        BimItAgentReadTools.ExploreSelectedMep,
        MepDevicePlacementProposalTool.Name,
    ];

    public static bool IsExposed(string? toolName)
    {
        return toolName is not null && Names.Contains(toolName, StringComparer.Ordinal);
    }
}
