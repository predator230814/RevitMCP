using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace BimIt.Revit.Addin.Intents;

internal static class MepDevicePlacementCanonicalEncoder
{
    internal const int SchemaVersion = 1;
    internal const string Domain = "mep_device_placements";
    internal const int MaxTextLength = 256;

    internal static bool TryEncode(
        string? instanceId,
        string? documentId,
        MepDevicePlacementIntent? intent,
        out byte[] canonical,
        out string fingerprint)
    {
        canonical = Array.Empty<byte>();
        fingerprint = string.Empty;
        if (instanceId is null
            || documentId is null
            || intent is null
            || !TryText(intent.LengthUnitId)
            || !TryText(intent.TemplateElementRef)
            || !TryText(intent.SymbolElementRef)
            || !TryText(intent.CategoryToken)
            || !TryText(intent.CategoryName)
            || !TryText(intent.FamilyName)
            || !TryText(intent.TypeName)
            || !TryText(intent.HostElementRef)
            || !TryText(intent.HostName)
            || !TryText(intent.LevelElementRef)
            || !TryText(intent.LevelName)
            || intent.Items is null
            || intent.Items.Count is < 1 or > 20)
        {
            return false;
        }

        var buffer = new List<byte>(512);
        WriteString(buffer, Domain);
        WriteUInt32(buffer, SchemaVersion);
        WriteString(buffer, instanceId);
        WriteString(buffer, documentId);
        WriteString(buffer, intent.LengthUnitId);
        WriteString(buffer, intent.TemplateElementRef);
        WriteString(buffer, intent.SymbolElementRef);
        WriteString(buffer, intent.CategoryToken);
        WriteString(buffer, intent.CategoryName);
        WriteString(buffer, intent.FamilyName);
        WriteString(buffer, intent.TypeName);
        WriteString(buffer, intent.HostElementRef);
        WriteString(buffer, intent.HostName);
        WriteString(buffer, intent.LevelElementRef);
        WriteString(buffer, intent.LevelName);
        if (!TryWritePoint(buffer, intent.TemplateLocation)
            || !TryWritePoint(buffer, intent.HostStart)
            || !TryWritePoint(buffer, intent.HostEnd))
        {
            return false;
        }

        WriteBool(buffer, intent.FacingFlipped);
        WriteBool(buffer, intent.HandFlipped);
        WriteBool(buffer, intent.CanFlipFacing);
        WriteBool(buffer, intent.CanFlipHand);
        WriteUInt32(buffer, (uint)intent.Items.Count);
        for (var index = 0; index < intent.Items.Count; index++)
        {
            var item = intent.Items[index];
            if (item is null
                || item.RequestPosition != index + 1
                || !double.IsFinite(item.OffsetAlongHost)
                || !double.IsFinite(item.VerticalOffset)
                || !TryWritePoint(buffer, item.Target))
            {
                return false;
            }

            WriteUInt32(buffer, (uint)item.RequestPosition);
            WriteDouble(buffer, CanonicalZero(item.OffsetAlongHost));
            WriteDouble(buffer, CanonicalZero(item.VerticalOffset));
        }

        canonical = buffer.ToArray();
        fingerprint = IntentCanonicalEncoder.ToLowerHex(SHA256.HashData(canonical));
        return true;
    }

    private static bool TryText(string? value)
    {
        return value is not null && value.Length > 0 && value.Length <= MaxTextLength;
    }

    private static bool TryWritePoint(List<byte> buffer, MepPoint point)
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y) || !double.IsFinite(point.Z))
        {
            return false;
        }

        WriteDouble(buffer, CanonicalZero(point.X));
        WriteDouble(buffer, CanonicalZero(point.Y));
        WriteDouble(buffer, CanonicalZero(point.Z));
        return true;
    }

    private static double CanonicalZero(double value)
    {
        return value == 0.0 ? 0.0 : value;
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

    private static void WriteDouble(List<byte> buffer, double value)
    {
        Span<byte> encoded = stackalloc byte[sizeof(double)];
        BinaryPrimitives.WriteInt64BigEndian(encoded, BitConverter.DoubleToInt64Bits(value));
        for (var index = 0; index < encoded.Length; index++)
        {
            buffer.Add(encoded[index]);
        }
    }
}
