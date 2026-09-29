using RevitMCP.Addin.Intents;
using RevitMCP.Bridge;
using RevitMCP.Contracts;

namespace RevitMCP.Addin.Inspection;

internal sealed class PreviewParameterUpdateCandidate
{
    public required string ElementRef { get; init; }

    public required string ParameterRef { get; init; }

    public required PreviewParameterUpdateStatus Status { get; init; }

    public string? ElementName { get; init; }

    public bool ElementNameTruncated { get; init; }

    public string? CategoryName { get; init; }

    public bool CategoryNameTruncated { get; init; }

    public string? ParameterName { get; init; }

    public bool ParameterNameTruncated { get; init; }

    public DescribeParameterDataType? DataType { get; init; }

    public bool BeforeHasValue { get; init; }

    public PreviewParameterValue? BeforeValue { get; init; }

    public PreviewParameterValue? Proposed { get; init; }

    public DescribeParameterIdentityKind IdentityKind { get; init; }

    public string? ParameterTypeId { get; init; }

    public string? SharedGuid { get; init; }

    public string? StableKey { get; init; }
}

internal static class PreviewParameterUpdateResults
{
    public static PreviewParameterUpdateItem ToPublicItem(PreviewParameterUpdateCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        if (candidate.Status is not PreviewParameterUpdateStatus.Ok and not PreviewParameterUpdateStatus.NoChange)
        {
            return new PreviewParameterUpdateItem
            {
                ElementRef = candidate.ElementRef,
                ParameterRef = candidate.ParameterRef,
                Status = candidate.Status
            };
        }

        return new PreviewParameterUpdateItem
        {
            ElementRef = candidate.ElementRef,
            ParameterRef = candidate.ParameterRef,
            Status = candidate.Status,
            ElementName = candidate.ElementName,
            ElementNameTruncated = candidate.ElementNameTruncated,
            CategoryName = candidate.CategoryName,
            CategoryNameTruncated = candidate.CategoryNameTruncated,
            ParameterName = candidate.ParameterName,
            ParameterNameTruncated = candidate.ParameterNameTruncated,
            DataType = candidate.DataType,
            Before = new PreviewParameterBefore
            {
                HasValue = candidate.BeforeHasValue,
                Value = candidate.BeforeHasValue ? candidate.BeforeValue : null
            },
            Proposed = candidate.Proposed
        };
    }

    public static IntentDraft Compose(
        string instanceId,
        string documentId,
        IReadOnlyList<PreviewParameterUpdateCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(instanceId);
        ArgumentNullException.ThrowIfNull(documentId);
        ArgumentNullException.ThrowIfNull(candidates);

        var items = new List<IntentItemDraft>(candidates.Count);
        for (var index = 0; index < candidates.Count; index++)
        {
            items.Add(ToIntentItem(candidates[index], index + 1));
        }

        return new IntentDraft
        {
            InstanceId = instanceId,
            DocumentId = documentId,
            Items = items
        };
    }

    public static PreviewParameterUpdatesResult Complete(
        DescribeParametersContext context,
        IReadOnlyList<PreviewParameterUpdateCandidate> candidates,
        Func<IntentDraft, IntentCreateResult> createIntent)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(createIntent);

        var items = new PreviewParameterUpdateItem[candidates.Count];
        var ready = candidates.Count > 0;
        for (var index = 0; index < candidates.Count; index++)
        {
            items[index] = ToPublicItem(candidates[index]);
            if (candidates[index].Status != PreviewParameterUpdateStatus.Ok)
            {
                ready = false;
            }
        }

        if (!ready)
        {
            return new PreviewParameterUpdatesResult
            {
                Context = context,
                Ready = false,
                Items = items
            };
        }

        var created = createIntent(Compose(context.InstanceId, context.DocumentId, candidates));
        if (created.Status == IntentStoreCreateStatus.Created
            && created.IntentRef is not null
            && created.IntentFingerprint is not null
            && created.ExpiresAt is not null)
        {
            return new PreviewParameterUpdatesResult
            {
                Context = context,
                Ready = true,
                Items = items,
                IntentRef = created.IntentRef,
                IntentFingerprint = created.IntentFingerprint,
                ExpiresAt = created.ExpiresAt
            };
        }

        if (created.Status == IntentStoreCreateStatus.CapacityReached)
        {
            throw new BridgeException(
                CapabilityErrorCodes.IntentCapacityReached,
                "The write-intent store is at capacity.");
        }

        throw new BridgeException(
            CapabilityErrorCodes.ExecutionFailed,
            "The Revit parameter update preview could not be executed.");
    }

    private static IntentItemDraft ToIntentItem(PreviewParameterUpdateCandidate candidate, int requestPosition)
    {
        if (candidate.Status != PreviewParameterUpdateStatus.Ok
            || candidate.ElementName is null
            || candidate.CategoryName is null
            || candidate.ParameterName is null
            || candidate.DataType is null
            || candidate.StableKey is null
            || candidate.Proposed is null
            || (candidate.BeforeHasValue && candidate.BeforeValue is null))
        {
            throw new BridgeException(
                CapabilityErrorCodes.ExecutionFailed,
                "The Revit parameter update preview could not be executed.");
        }

        return new IntentItemDraft
        {
            RequestPosition = requestPosition,
            ElementRef = candidate.ElementRef,
            ParameterRef = candidate.ParameterRef,
            Source = "instance",
            IdentityKind = candidate.IdentityKind,
            ParameterTypeId = candidate.ParameterTypeId,
            SharedGuid = candidate.SharedGuid,
            StableKey = candidate.StableKey,
            Status = "ok",
            ElementName = candidate.ElementName,
            ElementNameTruncated = candidate.ElementNameTruncated,
            CategoryName = candidate.CategoryName,
            CategoryNameTruncated = candidate.CategoryNameTruncated,
            ParameterName = candidate.ParameterName,
            ParameterNameTruncated = candidate.ParameterNameTruncated,
            DataTypeKind = candidate.DataType.Kind,
            ForgeTypeId = candidate.DataType.ForgeTypeId,
            BeforeHasValue = candidate.BeforeHasValue,
            BeforeValue = candidate.BeforeHasValue ? ToIntentValue(candidate.BeforeValue!) : null,
            Proposed = ToIntentValue(candidate.Proposed)
        };
    }

    internal static IntentTypedValue ToIntentValue(PreviewParameterValue value)
    {
        return value switch
        {
            PreviewParameterStringValue text => new IntentTypedValue.StringValue(text.Value),
            PreviewParameterIntegerValue integer => new IntentTypedValue.IntegerValue(integer.Value),
            PreviewParameterQuantityValue quantity => new IntentTypedValue.QuantityValue(quantity.Value, quantity.UnitTypeId),
            _ => throw new BridgeException(
                CapabilityErrorCodes.ExecutionFailed,
                "The Revit parameter update preview could not be executed.")
        };
    }
}
