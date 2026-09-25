using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using RevitMCP.Contracts;

namespace RevitMCP.Addin.Intents;

internal static class IntentCanonicalEncoder
{
    internal const int SchemaVersion = 1;
    internal const int MaxStringValueLength = 512;

    internal static bool TryEncode(
        IntentDraft draft,
        out string instanceId,
        out string documentId,
        out IReadOnlyList<IntentItemEntry> items,
        out byte[] canonical,
        out string fingerprint)
    {
        instanceId = string.Empty;
        documentId = string.Empty;
        items = Array.Empty<IntentItemEntry>();
        canonical = Array.Empty<byte>();
        fingerprint = string.Empty;

        if (draft.InstanceId is null || draft.DocumentId is null || draft.Items is null)
        {
            return false;
        }

        instanceId = draft.InstanceId;
        documentId = draft.DocumentId;

        var copied = new IntentItemEntry[draft.Items.Count];
        for (var index = 0; index < draft.Items.Count; index++)
        {
            if (!TryCopyItem(draft.Items[index], index + 1, out var item))
            {
                return false;
            }

            copied[index] = item;
        }

        var buffer = new List<byte>(256);
        WriteUInt32(buffer, SchemaVersion);
        WriteString(buffer, instanceId);
        WriteString(buffer, documentId);
        WriteUInt32(buffer, (uint)copied.Length);
        foreach (var item in copied)
        {
            if (!TryWriteItem(buffer, item))
            {
                return false;
            }
        }

        canonical = buffer.ToArray();
        fingerprint = ToLowerHex(SHA256.HashData(canonical));
        items = Array.AsReadOnly(copied);
        return true;
    }

    private static bool TryCopyItem(IntentItemDraft? draft, int expectedPosition, out IntentItemEntry item)
    {
        item = null!;
        if (draft is null
            || draft.RequestPosition != expectedPosition
            || draft.ElementRef is null
            || draft.ParameterRef is null
            || draft.Source != "instance"
            || !TryIdentityKind(draft.IdentityKind, out _)
            || !IsOpaqueOptional(draft.ParameterTypeId)
            || !IsOpaqueOptional(draft.SharedGuid)
            || draft.StableKey is null
            || draft.Status != "ok"
            || draft.ElementName is null
            || draft.CategoryName is null
            || draft.ParameterName is null
            || !TryDataTypeKind(draft.DataTypeKind, out _)
            || !IsOpaqueOptional(draft.ForgeTypeId)
            || !TryCopyValue(draft.BeforeHasValue, draft.BeforeValue, out var before)
            || draft.Proposed is null
            || !TryCopyRequiredValue(draft.Proposed, out var proposed))
        {
            return false;
        }

        item = new IntentItemEntry(
            draft.RequestPosition,
            draft.ElementRef,
            draft.ParameterRef,
            "instance",
            draft.IdentityKind,
            draft.ParameterTypeId,
            draft.SharedGuid,
            draft.StableKey,
            "ok",
            draft.ElementName,
            draft.ElementNameTruncated,
            draft.CategoryName,
            draft.CategoryNameTruncated,
            draft.ParameterName,
            draft.ParameterNameTruncated,
            draft.DataTypeKind,
            draft.ForgeTypeId,
            draft.BeforeHasValue,
            before,
            proposed);
        return true;
    }

    private static bool TryWriteItem(List<byte> buffer, IntentItemEntry item)
    {
        if (!TryIdentityKind(item.IdentityKind, out var identityKind)
            || !TryDataTypeKind(item.DataTypeKind, out var dataTypeKind))
        {
            return false;
        }

        WriteUInt32(buffer, (uint)item.RequestPosition);
        WriteString(buffer, item.ElementRef);
        WriteString(buffer, item.ParameterRef);
        WriteString(buffer, item.Source);
        WriteString(buffer, identityKind);
        WriteOptionalString(buffer, item.ParameterTypeId);
        WriteOptionalString(buffer, item.SharedGuid);
        WriteString(buffer, item.StableKey);
        WriteString(buffer, item.Status);
        WriteString(buffer, item.ElementName);
        WriteBool(buffer, item.ElementNameTruncated);
        WriteString(buffer, item.CategoryName);
        WriteBool(buffer, item.CategoryNameTruncated);
        WriteString(buffer, item.ParameterName);
        WriteBool(buffer, item.ParameterNameTruncated);
        WriteString(buffer, dataTypeKind);
        WriteOptionalString(buffer, item.ForgeTypeId);
        WriteBool(buffer, item.BeforeHasValue);
        if (item.BeforeHasValue)
        {
            if (item.BeforeValue is null || !TryWriteValue(buffer, item.BeforeValue))
            {
                return false;
            }
        }

        return TryWriteValue(buffer, item.Proposed);
    }

    private static bool TryCopyValue(bool hasValue, IntentTypedValue? value, out IntentTypedValue? copied)
    {
        copied = null;
        if (!hasValue)
        {
            return value is null;
        }

        return value is not null && TryCopyRequiredValue(value, out copied);
    }

    private static bool TryCopyRequiredValue(IntentTypedValue value, out IntentTypedValue copied)
    {
        switch (value)
        {
            case IntentTypedValue.StringValue text when text.Value is not null && text.Value.Length <= MaxStringValueLength:
                copied = new IntentTypedValue.StringValue(text.Value);
                return true;
            case IntentTypedValue.IntegerValue integer:
                copied = new IntentTypedValue.IntegerValue(integer.Value);
                return true;
            case IntentTypedValue.QuantityValue quantity
                when double.IsFinite(quantity.Value) && !string.IsNullOrEmpty(quantity.UnitTypeId):
                copied = new IntentTypedValue.QuantityValue(CanonicalZero(quantity.Value), quantity.UnitTypeId);
                return true;
            default:
                copied = null!;
                return false;
        }
    }

    private static bool TryWriteValue(List<byte> buffer, IntentTypedValue value)
    {
        switch (value)
        {
            case IntentTypedValue.StringValue text:
                buffer.Add(1);
                WriteString(buffer, text.Value);
                return true;
            case IntentTypedValue.IntegerValue integer:
                buffer.Add(2);
                WriteInt32(buffer, integer.Value);
                return true;
            case IntentTypedValue.QuantityValue quantity when double.IsFinite(quantity.Value) && quantity.UnitTypeId.Length > 0:
                buffer.Add(3);
                WriteDouble(buffer, CanonicalZero(quantity.Value));
                WriteString(buffer, quantity.UnitTypeId);
                return true;
            default:
                return false;
        }
    }

    private static double CanonicalZero(double value)
    {
        return value == 0.0 ? 0.0 : value;
    }

    private static bool IsOpaqueOptional(string? value)
    {
        return value is null || value.Length > 0;
    }

    private static bool TryIdentityKind(DescribeParameterIdentityKind kind, out string token)
    {
        switch (kind)
        {
            case DescribeParameterIdentityKind.BuiltIn:
                token = "built_in";
                return true;
            case DescribeParameterIdentityKind.Shared:
                token = "shared";
                return true;
            case DescribeParameterIdentityKind.Local:
                token = "local";
                return true;
            default:
                token = string.Empty;
                return false;
        }
    }

    private static bool TryDataTypeKind(DescribeParameterDataTypeKind kind, out string token)
    {
        switch (kind)
        {
            case DescribeParameterDataTypeKind.MeasurableSpec:
                token = "measurable_spec";
                return true;
            case DescribeParameterDataTypeKind.Spec:
                token = "spec";
                return true;
            case DescribeParameterDataTypeKind.Category:
                token = "category";
                return true;
            case DescribeParameterDataTypeKind.Unknown:
                token = "unknown";
                return true;
            default:
                token = string.Empty;
                return false;
        }
    }

    private static void WriteOptionalString(List<byte> buffer, string? value)
    {
        if (value is null)
        {
            buffer.Add(0);
            return;
        }

        buffer.Add(1);
        WriteString(buffer, value);
    }

    private static void WriteString(List<byte> buffer, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        WriteUInt32(buffer, (uint)bytes.Length);
        buffer.AddRange(bytes);
    }

    private static void WriteBool(List<byte> buffer, bool value)
    {
        buffer.Add(value ? (byte)1 : (byte)0);
    }

    private static void WriteUInt32(List<byte> buffer, uint value)
    {
        Span<byte> encoded = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32BigEndian(encoded, value);
        buffer.Add(encoded[0]);
        buffer.Add(encoded[1]);
        buffer.Add(encoded[2]);
        buffer.Add(encoded[3]);
    }

    private static void WriteInt32(List<byte> buffer, int value)
    {
        Span<byte> encoded = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(encoded, value);
        buffer.Add(encoded[0]);
        buffer.Add(encoded[1]);
        buffer.Add(encoded[2]);
        buffer.Add(encoded[3]);
    }

    private static void WriteDouble(List<byte> buffer, double value)
    {
        Span<byte> encoded = stackalloc byte[sizeof(double)];
        BinaryPrimitives.WriteInt64BigEndian(encoded, BitConverter.DoubleToInt64Bits(value));
        for (var index = 0; index < encoded.Length; index++)
        {
            buffer.Add(encoded[index]);
        }
    }

    internal static string ToLowerHex(ReadOnlySpan<byte> bytes)
    {
        const string alphabet = "0123456789abcdef";
        var chars = new char[bytes.Length * 2];
        for (var index = 0; index < bytes.Length; index++)
        {
            chars[index * 2] = alphabet[bytes[index] >> 4];
            chars[(index * 2) + 1] = alphabet[bytes[index] & 0x0F];
        }

        return new string(chars);
    }
}
