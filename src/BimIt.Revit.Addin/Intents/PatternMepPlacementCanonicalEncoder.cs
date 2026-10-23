using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace BimIt.Revit.Addin.Intents;

internal static class PatternMepPlacementCanonicalEncoder
{
    internal const int SchemaVersion = 1;

    internal const string Domain = "pattern_mep_placements";

    internal const int MaxTextLength = 256;

    internal static bool TryEncode(
        string? instanceId,
        string? documentId,
        PatternMepPlacementIntent? intent,
        out byte[] canonical,
        out string fingerprint)
    {
        canonical = [];
        fingerprint = string.Empty;
        if (instanceId is null
            || documentId is null
            || intent is null
            || intent.Rooms is null
            || intent.Items is null
            || intent.Rooms.Count is < 1 or > PatternPlacementComposer.MaxRooms
            || intent.Items.Count is < 1 or > PatternPlacementComposer.MaxPlacements)
        {
            return false;
        }

        var buffer = new List<byte>(1024);
        WriteString(buffer, Domain);
        WriteUInt32(buffer, SchemaVersion);
        WriteString(buffer, instanceId);
        WriteString(buffer, documentId);
        WriteUInt32(buffer, (uint)intent.Rooms.Count);
        foreach (var room in intent.Rooms)
        {
            if (!TryText(room.RoomRef) || !TryText(room.Label) || !TryText(room.LevelRef) || !TryText(room.LevelName) || room.Loop is null || room.Loop.Count is < 3 or > 32)
            {
                return false;
            }

            WriteString(buffer, room.RoomRef);
            WriteString(buffer, room.Label);
            WriteString(buffer, room.LevelRef);
            WriteString(buffer, room.LevelName);
            WriteDouble(buffer, CanonicalZero(room.LevelElevationFeet));
            WriteUInt32(buffer, (uint)room.Loop.Count);
            foreach (var point in room.Loop)
            {
                if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
                {
                    return false;
                }

                WriteDouble(buffer, CanonicalZero(point.X));
                WriteDouble(buffer, CanonicalZero(point.Y));
            }
        }

        WriteUInt32(buffer, (uint)intent.Items.Count);
        for (var index = 0; index < intent.Items.Count; index++)
        {
            var item = intent.Items[index];
            if (item is null
                || item.Position != index + 1
                || !TryText(item.RoomRef)
                || !TryText(item.SymbolRef)
                || !TryText(item.CategoryToken)
                || !TryText(item.CategoryName)
                || !TryText(item.FamilyName)
                || !TryText(item.TypeName)
                || !TryText(item.Role)
                || !TryText(item.PlacementKind)
                || !TryText(item.LevelRef)
                || !double.IsFinite(item.Target.X)
                || !double.IsFinite(item.Target.Y)
                || !double.IsFinite(item.Target.Z)
                || !double.IsFinite(item.MountingHeightFeet)
                || item.QuarterTurns is < 0 or > 3
                || !StrategyComplete(item))
            {
                return false;
            }

            WriteUInt32(buffer, (uint)item.Position);
            WriteString(buffer, item.RoomRef);
            WriteString(buffer, item.SymbolRef);
            WriteString(buffer, item.CategoryToken);
            WriteString(buffer, item.CategoryName);
            WriteString(buffer, item.FamilyName);
            WriteString(buffer, item.TypeName);
            WriteString(buffer, item.Role);
            WriteString(buffer, item.PlacementKind);
            WriteUInt32(buffer, (uint)item.Strategy);
            WriteString(buffer, item.LevelRef);
            WriteDouble(buffer, CanonicalZero(item.Target.X));
            WriteDouble(buffer, CanonicalZero(item.Target.Y));
            WriteDouble(buffer, CanonicalZero(item.Target.Z));
            WriteDouble(buffer, CanonicalZero(item.MountingHeightFeet));
            WriteUInt32(buffer, (uint)item.QuarterTurns);
            WriteDouble(buffer, CanonicalZero(item.ReferenceDirection.X));
            WriteDouble(buffer, CanonicalZero(item.ReferenceDirection.Y));
            if (item.Host is { } host)
            {
                WriteString(buffer, host.WallRef);
                WriteString(buffer, host.WallName);
                WriteDouble(buffer, CanonicalZero(host.Start.X));
                WriteDouble(buffer, CanonicalZero(host.Start.Y));
                WriteDouble(buffer, CanonicalZero(host.Start.Z));
                WriteDouble(buffer, CanonicalZero(host.End.X));
                WriteDouble(buffer, CanonicalZero(host.End.Y));
                WriteDouble(buffer, CanonicalZero(host.End.Z));
                WriteDouble(buffer, CanonicalZero(host.InsideNormal.X));
                WriteDouble(buffer, CanonicalZero(host.InsideNormal.Y));
            }
        }

        canonical = buffer.ToArray();
        fingerprint = IntentCanonicalEncoder.ToLowerHex(SHA256.HashData(canonical));
        return true;
    }

    private static bool StrategyComplete(PatternMepPlacementItem item)
    {
        var interior = item.Strategy is PatternPlacementStrategyKind.InteriorLevel or PatternPlacementStrategyKind.InteriorLevelWorkPlane;
        if (interior)
        {
            return item.Host is null && item.PlacementKind == "interior";
        }

        return item.PlacementKind == "boundary"
            && item.Host is { } host
            && TryText(host.WallRef)
            && TryText(host.WallName)
            && double.IsFinite(host.InsideNormal.X)
            && double.IsFinite(host.InsideNormal.Y);
    }

    private static bool TryText(string? value) => value is not null && value.Length > 0 && value.Length <= MaxTextLength;

    private static double CanonicalZero(double value) => value == 0.0 ? 0.0 : value;

    private static void WriteString(List<byte> buffer, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        WriteUInt32(buffer, (uint)bytes.Length);
        buffer.AddRange(bytes);
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
        buffer.AddRange(encoded.ToArray());
    }
}
