using RevitMCP.Contracts;

namespace RevitMCP.Addin.Intents;

internal sealed class IntentItemDraft
{
    public int RequestPosition { get; set; }

    public string? ElementRef { get; set; }

    public string? ParameterRef { get; set; }

    public string? Source { get; set; }

    public DescribeParameterIdentityKind IdentityKind { get; set; }

    public string? ParameterTypeId { get; set; }

    public string? SharedGuid { get; set; }

    public string? StableKey { get; set; }

    public string? Status { get; set; }

    public string? ElementName { get; set; }

    public bool ElementNameTruncated { get; set; }

    public string? CategoryName { get; set; }

    public bool CategoryNameTruncated { get; set; }

    public string? ParameterName { get; set; }

    public bool ParameterNameTruncated { get; set; }

    public DescribeParameterDataTypeKind DataTypeKind { get; set; }

    public string? ForgeTypeId { get; set; }

    public bool BeforeHasValue { get; set; }

    public IntentTypedValue? BeforeValue { get; set; }

    public IntentTypedValue? Proposed { get; set; }
}

internal sealed class IntentDraft
{
    public string? InstanceId { get; set; }

    public string? DocumentId { get; set; }

    public List<IntentItemDraft>? Items { get; set; }
}
