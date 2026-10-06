using RevitMCP.Addin.Intents;
using RevitMCP.Contracts;

namespace RevitMCP.Addin.Approval;

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
        IReadOnlyList<ApprovalReviewItemModel> items)
    {
        SessionRef = sessionRef;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
        Items = items;
    }

    public string SessionRef { get; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset ExpiresAt { get; }

    public IReadOnlyList<ApprovalReviewItemModel> Items { get; }
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
