using BimIt.Revit.Addin.Intents;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Addin.Approval;

/// <summary>
/// Immutable human-review presentation built only from Addin-owned intent state.
/// It carries session correlation, not approval authority.
/// </summary>
internal sealed class ApprovalReviewRenderModel
{
    public ApprovalReviewRenderModel(
        string sessionRef,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt,
        IReadOnlyList<ApprovalReviewItemModel> items,
        ApprovalMepDeviceReviewModel? mepDevices = null,
        ApprovalPatternPlacementReviewModel? patternPlacements = null)
    {
        SessionRef = sessionRef;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
        Items = items;
        MepDevices = mepDevices;
        PatternPlacements = patternPlacements;
    }

    public string SessionRef { get; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset ExpiresAt { get; }

    public IReadOnlyList<ApprovalReviewItemModel> Items { get; }

    public ApprovalMepDeviceReviewModel? MepDevices { get; }

    public ApprovalPatternPlacementReviewModel? PatternPlacements { get; }
}

internal sealed class ApprovalPatternPlacementReviewModel
{
    public ApprovalPatternPlacementReviewModel(int roomCount, int deviceCount, IReadOnlyList<string> lines, IReadOnlyList<string> roomLabels)
    {
        RoomCount = roomCount;
        DeviceCount = deviceCount;
        Lines = lines;
        RoomLabels = roomLabels;
    }

    public int RoomCount { get; }

    public int DeviceCount { get; }

    public IReadOnlyList<string> Lines { get; }

    public IReadOnlyList<string> RoomLabels { get; }
}

internal sealed class ApprovalMepDeviceReviewModel
{
    public ApprovalMepDeviceReviewModel(
        string placementLabel,
        string categoryName,
        string familyName,
        string typeName,
        string hostName,
        string levelName,
        IReadOnlyList<string> placementLines)
    {
        PlacementLabel = placementLabel;
        CategoryName = categoryName;
        FamilyName = familyName;
        TypeName = typeName;
        HostName = hostName;
        LevelName = levelName;
        PlacementLines = placementLines;
    }

    public string PlacementLabel { get; }

    public string CategoryName { get; }

    public string FamilyName { get; }

    public string TypeName { get; }

    public string HostName { get; }

    public string LevelName { get; }

    public IReadOnlyList<string> PlacementLines { get; }
}

internal sealed class ApprovalReviewItemModel
{
    public ApprovalReviewItemModel(
        int requestPosition,
        string elementName,
        bool elementNameTruncated,
        string categoryName,
        bool categoryNameTruncated,
        string parameterName,
        bool parameterNameTruncated,
        ApprovalReviewDataTypeModel dataType,
        ApprovalReviewBeforeModel before,
        ApprovalReviewValueModel proposed)
    {
        RequestPosition = requestPosition;
        ElementName = elementName;
        ElementNameTruncated = elementNameTruncated;
        CategoryName = categoryName;
        CategoryNameTruncated = categoryNameTruncated;
        ParameterName = parameterName;
        ParameterNameTruncated = parameterNameTruncated;
        DataType = dataType;
        Before = before;
        Proposed = proposed;
    }

    public int RequestPosition { get; }

    public string ElementName { get; }

    public bool ElementNameTruncated { get; }

    public string CategoryName { get; }

    public bool CategoryNameTruncated { get; }

    public string ParameterName { get; }

    public bool ParameterNameTruncated { get; }

    public ApprovalReviewDataTypeModel DataType { get; }

    public ApprovalReviewBeforeModel Before { get; }

    public ApprovalReviewValueModel Proposed { get; }
}

internal sealed class ApprovalReviewDataTypeModel
{
    public ApprovalReviewDataTypeModel(DescribeParameterDataTypeKind kind, string? forgeTypeId)
    {
        Kind = kind;
        ForgeTypeId = forgeTypeId;
    }

    public DescribeParameterDataTypeKind Kind { get; }

    public string? ForgeTypeId { get; }
}

internal sealed class ApprovalReviewBeforeModel
{
    public ApprovalReviewBeforeModel(bool hasValue, ApprovalReviewValueModel? value)
    {
        HasValue = hasValue;
        Value = value;
    }

    public bool HasValue { get; }

    public ApprovalReviewValueModel? Value { get; }
}

internal abstract record ApprovalReviewValueModel
{
    private ApprovalReviewValueModel()
    {
    }

    internal sealed record StringValue(string Value) : ApprovalReviewValueModel;

    internal sealed record IntegerValue(int Value) : ApprovalReviewValueModel;

    internal sealed record QuantityValue(double Value, string UnitTypeId) : ApprovalReviewValueModel;
}

internal static class ApprovalReviewRenderer
{
    internal static bool TryCreate(string sessionRef, IntentEntry entry, out ApprovalReviewRenderModel model)
    {
        model = null!;
        if (string.IsNullOrWhiteSpace(sessionRef)
            || entry.FingerprintSchemaVersion != IntentCanonicalEncoder.SchemaVersion
            || entry.Items is null
            || entry.Items.Count == 0)
        {
            return false;
        }

        var rendered = new ApprovalReviewItemModel[entry.Items.Count];
        for (var index = 0; index < entry.Items.Count; index++)
        {
            if (!TryRenderItem(entry.Items[index], index + 1, out var item))
            {
                return false;
            }

            rendered[index] = item;
        }

        model = new ApprovalReviewRenderModel(
            sessionRef,
            entry.CreatedAt,
            entry.ExpiresAt,
            Array.AsReadOnly(rendered));
        return true;
    }

    internal static bool TryCreateMep(
        string sessionRef,
        ControlledWriteIntentBinding binding,
        MepDevicePlacementIntent intent,
        out ApprovalReviewRenderModel model)
    {
        model = null!;
        if (string.IsNullOrWhiteSpace(sessionRef)
            || binding.Kind != ControlledWriteIntentKind.MepDevicePlacements
            || binding.FingerprintSchemaVersion != MepDevicePlacementCanonicalEncoder.SchemaVersion
            || intent.Items is null
            || intent.Items.Count == 0
            || intent.Items.Count != binding.ItemCount)
        {
            return false;
        }

        var lines = new string[intent.Items.Count];
        var unit = MepLengthUnitLabel.Describe(intent.LengthUnitId);
        for (var index = 0; index < intent.Items.Count; index++)
        {
            var item = intent.Items[index];
            if (item is null || item.RequestPosition != index + 1)
            {
                return false;
            }

            lines[index] = string.Concat(
                item.RequestPosition.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ". X ",
                MepLengthUnitLabel.Signed(item.OffsetX),
                " ",
                unit,
                ", Y ",
                MepLengthUnitLabel.Signed(item.OffsetY),
                " ",
                unit,
                ", vertical ",
                MepLengthUnitLabel.Signed(item.VerticalOffset),
                " ",
                unit);
        }

        model = new ApprovalReviewRenderModel(
            sessionRef,
            binding.CreatedAt,
            binding.ExpiresAt,
            Array.Empty<ApprovalReviewItemModel>(),
            new ApprovalMepDeviceReviewModel(
                PlacementLabel(intent.Strategy),
                intent.CategoryName,
                intent.FamilyName,
                intent.TypeName,
                intent.Wall?.HostName ?? string.Empty,
                intent.LevelName,
                Array.AsReadOnly(lines)));
        return true;
    }

    internal static bool TryCreatePattern(
        string sessionRef,
        ControlledWriteIntentBinding binding,
        PatternMepPlacementIntent intent,
        out ApprovalReviewRenderModel model)
    {
        model = null!;
        if (string.IsNullOrWhiteSpace(sessionRef)
            || binding.Kind != ControlledWriteIntentKind.MepDevicePlacements
            || binding.FingerprintSchemaVersion != PatternMepPlacementCanonicalEncoder.SchemaVersion
            || intent.Items is null
            || intent.Rooms is null
            || intent.Items.Count == 0
            || intent.Items.Count != binding.ItemCount
            || intent.Rooms.Count is < 1 or > 8)
        {
            return false;
        }

        var groups = intent.Items
            .GroupBy(item => (item.Role, item.FamilyName, item.TypeName, Millimetres(item.MountingHeightFeet)))
            .ToArray();
        var lines = new string[groups.Length];
        for (var index = 0; index < groups.Length; index++)
        {
            var group = groups[index];
            var key = group.Key;
            lines[index] = string.Concat(
                DisplayRole(key.Role),
                "\n",
                key.FamilyName,
                " / ",
                key.TypeName,
                "\n",
                group.Count().ToString(System.Globalization.CultureInfo.InvariantCulture),
                " placements · ",
                key.Item4.ToString(System.Globalization.CultureInfo.InvariantCulture),
                " mm");
        }

        var labels = intent.Rooms
            .Select(room => room.Label)
            .Where(label => !string.IsNullOrWhiteSpace(label) && label.Length <= 120)
            .Distinct(StringComparer.Ordinal)
            .Take(8)
            .ToArray();
        model = new ApprovalReviewRenderModel(
            sessionRef,
            binding.CreatedAt,
            binding.ExpiresAt,
            Array.Empty<ApprovalReviewItemModel>(),
            patternPlacements: new ApprovalPatternPlacementReviewModel(intent.Rooms.Count, intent.Items.Count, lines, labels));
        return true;
    }

    private static int Millimetres(double feet)
    {
        return (int)Math.Round(feet * 304.8, MidpointRounding.AwayFromZero);
    }

    private static string DisplayRole(string role)
    {
        return role switch
        {
            "receptacle" => "Receptacle",
            "switch" => "Switch",
            "luminaire" => "Luminaire",
            "smoke_detector" => "Smoke detector",
            "thermostat" => "Thermostat",
            "data" => "Data",
            _ => "Other",
        };
    }

    private static string PlacementLabel(MepPlacementStrategyKind strategy)
    {
        return strategy switch
        {
            MepPlacementStrategyKind.LevelBased => "Level based",
            MepPlacementStrategyKind.WallHosted => "Wall hosted",
            MepPlacementStrategyKind.WorkPlaneBasedLevel => "Level work plane",
            _ => string.Empty
        };
    }

    private static bool TryRenderItem(IntentItemEntry? item, int expectedPosition, out ApprovalReviewItemModel rendered)
    {
        rendered = null!;
        if (item is null
            || item.RequestPosition != expectedPosition
            || !string.Equals(item.Status, "ok", StringComparison.Ordinal)
            || item.ElementName is null
            || item.CategoryName is null
            || item.ParameterName is null
            || !TryDataType(item.DataTypeKind, item.ForgeTypeId, out var dataType)
            || !TryCopyValue(item.BeforeHasValue, item.BeforeValue, out var before)
            || !TryCopyRequiredValue(item.Proposed, out var proposed))
        {
            return false;
        }

        rendered = new ApprovalReviewItemModel(
            item.RequestPosition,
            item.ElementName,
            item.ElementNameTruncated,
            item.CategoryName,
            item.CategoryNameTruncated,
            item.ParameterName,
            item.ParameterNameTruncated,
            dataType,
            new ApprovalReviewBeforeModel(item.BeforeHasValue, before),
            proposed);
        return true;
    }

    private static bool TryDataType(DescribeParameterDataTypeKind kind, string? forgeTypeId, out ApprovalReviewDataTypeModel dataType)
    {
        dataType = null!;
        if (!Enum.IsDefined(kind))
        {
            return false;
        }

        dataType = new ApprovalReviewDataTypeModel(kind, forgeTypeId);
        return true;
    }

    private static bool TryCopyValue(bool hasValue, IntentTypedValue? value, out ApprovalReviewValueModel? copied)
    {
        copied = null;
        if (!hasValue)
        {
            return value is null;
        }

        return value is not null && TryCopyRequiredValue(value, out copied);
    }

    private static bool TryCopyRequiredValue(IntentTypedValue? value, out ApprovalReviewValueModel copied)
    {
        switch (value)
        {
            case IntentTypedValue.StringValue text when text.Value is not null && text.Value.Length <= IntentCanonicalEncoder.MaxStringValueLength:
                copied = new ApprovalReviewValueModel.StringValue(text.Value);
                return true;
            case IntentTypedValue.IntegerValue integer:
                copied = new ApprovalReviewValueModel.IntegerValue(integer.Value);
                return true;
            case IntentTypedValue.QuantityValue quantity
                when double.IsFinite(quantity.Value) && !string.IsNullOrEmpty(quantity.UnitTypeId):
                copied = new ApprovalReviewValueModel.QuantityValue(quantity.Value, quantity.UnitTypeId);
                return true;
            default:
                copied = null!;
                return false;
        }
    }
}
