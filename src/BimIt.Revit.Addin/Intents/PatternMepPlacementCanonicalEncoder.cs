using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace BimIt.Revit.Addin.Intents;

internal static class PatternMepPlacementCanonicalEncoder
{
    internal const int SchemaVersion = 2;

    internal const string Domain = "pattern_mep_placements";

    internal const int MaxTextLength = 256;

    internal const int MaxHoles = PatternLinkedRoomFactsBuilder.MaxLoops - 1;

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
            if (room is null || !TryRoom(buffer, room))
            {
                return false;
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
                || !(TryText(item.LevelRef) || (item.Strategy == PatternPlacementStrategyKind.BoundaryLinkedWallFace && item.LevelRef == string.Empty))
                || !double.IsFinite(item.Target.X)
                || !double.IsFinite(item.Target.Y)
                || !double.IsFinite(item.Target.Z)
                || !double.IsFinite(item.MountingHeightFeet)
                || !double.IsFinite(item.Requested.X)
                || !double.IsFinite(item.Requested.Y)
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
            WriteDouble(buffer, item.Target.X);
            WriteDouble(buffer, item.Target.Y);
            WriteDouble(buffer, item.Target.Z);
            WriteDouble(buffer, item.Requested.X);
            WriteDouble(buffer, item.Requested.Y);
            WriteDouble(buffer, item.MountingHeightFeet);
            WriteUInt32(buffer, (uint)item.QuarterTurns);
            WriteDouble(buffer, item.ReferenceDirection.X);
            WriteDouble(buffer, item.ReferenceDirection.Y);
            if (item.Host is { } host)
            {
                WriteUInt32(buffer, host.LinkedFace is null ? 1u : 2u);
                WriteString(buffer, host.WallRef);
                WriteString(buffer, host.WallName);
                WriteDouble(buffer, host.Start.X);
                WriteDouble(buffer, host.Start.Y);
                WriteDouble(buffer, host.Start.Z);
                WriteDouble(buffer, host.End.X);
                WriteDouble(buffer, host.End.Y);
                WriteDouble(buffer, host.End.Z);
                WriteDouble(buffer, host.InsideNormal.X);
                WriteDouble(buffer, host.InsideNormal.Y);
                if (host.LinkedFace is { } face)
                {
                    WriteString(buffer, host.LinkInstanceRef!);
                    WriteUInt32(buffer, (uint)face.Side);
                    WriteDouble(buffer, face.NormalX);
                    WriteDouble(buffer, face.NormalY);
                    WriteDouble(buffer, face.PlaneOffset);
                }
            }
            else
            {
                WriteUInt32(buffer, 0);
            }
        }

        canonical = buffer.ToArray();
        fingerprint = IntentCanonicalEncoder.ToLowerHex(SHA256.HashData(canonical));
        return true;
    }

    private static bool TryRoom(List<byte> buffer, PatternMepRoomBinding room)
    {
        var linked = room.SourceKind == PatternRoomSourceKind.Link;
        if (!TryText(room.RoomRef)
            || !TryText(room.Label)
            || !(TryText(room.LevelRef) || (linked && room.LevelRef == string.Empty && room.HostLevelStatus != PatternHostLevelStatus.Resolved))
            || !TryText(room.LevelName)
            || !double.IsFinite(room.LevelElevationFeet)
            || !TryLoop(room.Loop)
            || room.Holes is null
            || room.Holes.Count > MaxHoles
            || room.Holes.Any(hole => !TryLoop(hole))
            || room.SourceKind is not (PatternRoomSourceKind.Host or PatternRoomSourceKind.Link)
            || room.HostLevelStatus is not (PatternHostLevelStatus.Resolved or PatternHostLevelStatus.NotFound or PatternHostLevelStatus.Ambiguous))
        {
            return false;
        }

        if (linked
            ? room.Link is not { } link
                || !TryText(link.LinkInstanceRef)
                || !TryText(link.LinkedRoomRef)
                || !TryText(link.LinkLabel)
                || !link.Transform.IsPlanRigid
            : room.Link is not null || room.Holes.Count > 0 || room.HostLevelStatus != PatternHostLevelStatus.Resolved)
        {
            return false;
        }

        WriteUInt32(buffer, (uint)room.SourceKind);
        WriteString(buffer, room.RoomRef);
        WriteString(buffer, room.Label);
        WriteString(buffer, room.LevelRef);
        WriteString(buffer, room.LevelName);
        WriteDouble(buffer, room.LevelElevationFeet);
        WriteUInt32(buffer, (uint)room.HostLevelStatus);
        WriteLoop(buffer, room.Loop);
        WriteUInt32(buffer, (uint)room.Holes.Count);
        foreach (var hole in room.Holes)
        {
            WriteLoop(buffer, hole);
        }

        if (room.Link is { } source)
        {
            WriteString(buffer, source.LinkInstanceRef);
            WriteString(buffer, source.LinkedRoomRef);
            WriteString(buffer, source.LinkLabel);
            foreach (var value in source.Transform.Values)
            {
                WriteDouble(buffer, value);
            }
        }

        return true;
    }

    private static bool TryLoop(IReadOnlyList<PatternPlanPoint>? loop)
    {
        return loop is not null
            && loop.Count is >= 3 and <= 32
            && loop.All(point => double.IsFinite(point.X) && double.IsFinite(point.Y));
    }

    private static void WriteLoop(List<byte> buffer, IReadOnlyList<PatternPlanPoint> loop)
    {
        WriteUInt32(buffer, (uint)loop.Count);
        foreach (var point in loop)
        {
            WriteDouble(buffer, point.X);
            WriteDouble(buffer, point.Y);
        }
    }

    private static bool StrategyComplete(PatternMepPlacementItem item)
    {
        var interior = item.Strategy is PatternPlacementStrategyKind.InteriorLevel or PatternPlacementStrategyKind.InteriorLevelWorkPlane;
        if (interior)
        {
            return item.Host is null && item.PlacementKind == "interior";
        }

        if (item.PlacementKind != "boundary"
            || item.Host is not { } host
            || !TryText(host.WallRef)
            || !TryText(host.WallName)
            || !double.IsFinite(host.InsideNormal.X)
            || !double.IsFinite(host.InsideNormal.Y))
        {
            return false;
        }

        var linkedLevel = item.Strategy == PatternPlacementStrategyKind.BoundaryLevel && host.LinkInstanceRef is not null;
        if (item.Strategy == PatternPlacementStrategyKind.BoundaryLinkedWallFace || linkedLevel)
        {
            return TryText(host.LinkInstanceRef)
                && host.LinkedFace is { } face
                && face.Side is PatternShellSide.Interior or PatternShellSide.Exterior
                && double.IsFinite(face.NormalX)
                && double.IsFinite(face.NormalY)
                && double.IsFinite(face.PlaneOffset);
        }

        return item.Strategy is PatternPlacementStrategyKind.BoundaryWallHosted
                or PatternPlacementStrategyKind.BoundaryWallFace
                or PatternPlacementStrategyKind.BoundaryLevel
            && host.LinkInstanceRef is null
            && host.LinkedFace is null;
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
        BinaryPrimitives.WriteInt64BigEndian(encoded, BitConverter.DoubleToInt64Bits(CanonicalZero(value)));
        buffer.AddRange(encoded.ToArray());
    }
}
