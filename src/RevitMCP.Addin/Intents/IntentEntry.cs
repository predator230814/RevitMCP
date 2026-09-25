using RevitMCP.Contracts;

namespace RevitMCP.Addin.Intents;

internal sealed class IntentItemEntry
{
    public IntentItemEntry(
        int requestPosition,
        string elementRef,
        string parameterRef,
        string source,
        DescribeParameterIdentityKind identityKind,
        string? parameterTypeId,
        string? sharedGuid,
        string stableKey,
        string status,
        string elementName,
        bool elementNameTruncated,
        string categoryName,
        bool categoryNameTruncated,
        string parameterName,
        bool parameterNameTruncated,
        DescribeParameterDataTypeKind dataTypeKind,
        string? forgeTypeId,
        bool beforeHasValue,
        IntentTypedValue? beforeValue,
        IntentTypedValue proposed)
    {
        RequestPosition = requestPosition;
        ElementRef = elementRef;
        ParameterRef = parameterRef;
        Source = source;
        IdentityKind = identityKind;
        ParameterTypeId = parameterTypeId;
        SharedGuid = sharedGuid;
        StableKey = stableKey;
        Status = status;
        ElementName = elementName;
        ElementNameTruncated = elementNameTruncated;
        CategoryName = categoryName;
        CategoryNameTruncated = categoryNameTruncated;
        ParameterName = parameterName;
        ParameterNameTruncated = parameterNameTruncated;
        DataTypeKind = dataTypeKind;
        ForgeTypeId = forgeTypeId;
        BeforeHasValue = beforeHasValue;
        BeforeValue = beforeValue;
        Proposed = proposed;
    }

    public int RequestPosition { get; }

    public string ElementRef { get; }

    public string ParameterRef { get; }

    public string Source { get; }

    public DescribeParameterIdentityKind IdentityKind { get; }

    public string? ParameterTypeId { get; }

    public string? SharedGuid { get; }

    public string StableKey { get; }

    public string Status { get; }

    public string ElementName { get; }

    public bool ElementNameTruncated { get; }

    public string CategoryName { get; }

    public bool CategoryNameTruncated { get; }

    public string ParameterName { get; }

    public bool ParameterNameTruncated { get; }

    public DescribeParameterDataTypeKind DataTypeKind { get; }

    public string? ForgeTypeId { get; }

    public bool BeforeHasValue { get; }

    public IntentTypedValue? BeforeValue { get; }

    public IntentTypedValue Proposed { get; }
}

internal sealed class IntentEntry
{
    public IntentEntry(
        string intentRef,
        string intentFingerprint,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt,
        string instanceId,
        string documentId,
        IReadOnlyList<IntentItemEntry> items)
    {
        IntentRef = intentRef;
        IntentFingerprint = intentFingerprint;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
        InstanceId = instanceId;
        DocumentId = documentId;
        Items = items;
    }

    public string IntentRef { get; }

    public string IntentFingerprint { get; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset ExpiresAt { get; }

    public string InstanceId { get; }

    public string DocumentId { get; }

    public IReadOnlyList<IntentItemEntry> Items { get; }
}
