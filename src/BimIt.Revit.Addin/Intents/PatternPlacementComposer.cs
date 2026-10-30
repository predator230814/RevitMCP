using BimIt.Revit.Contracts;

namespace BimIt.Revit.Addin.Intents;

internal static class PatternPlacementComposer
{
    public const int MaxRooms = 8;

    public const int MaxPlacements = 80;

    public const double MaxMountingFeet = 6.0 * MepDevicePlacementGeometry.MetresToFeet;

    public const double WallToleranceFeet = 0.15 * MepDevicePlacementGeometry.MetresToFeet;

    public const double LinkedBoundaryToleranceFeet = 0.2 * MepDevicePlacementGeometry.MetresToFeet;

    public static bool TryCompose(
        IReadOnlyList<PatternPlacementDraft> drafts,
        PatternProjectFacts facts,
        out PatternMepPlacementIntent? intent,
        out IReadOnlyList<PreviewRoomPatternItemStatus> statuses)
    {
        intent = null;
        var results = new PreviewRoomPatternItemStatus[drafts.Count];
        if (!facts.DocumentMatches || !facts.OccupancyReadSucceeded || drafts.Count is < 1 or > MaxPlacements)
        {
            for (var index = 0; index < results.Length; index++)
            {
                results[index] = !facts.DocumentMatches
                    ? PreviewRoomPatternItemStatus.DocumentMismatch
                    : PreviewRoomPatternItemStatus.Occupied;
            }

            statuses = results;
            return false;
        }

        var rooms = new Dictionary<string, PatternMepRoomBinding>(StringComparer.Ordinal);
        var items = new List<PatternMepPlacementItem>(drafts.Count);
        var batchOccupants = new List<PatternOccupant>();
        var ready = true;
        for (var index = 0; index < drafts.Count; index++)
        {
            if (!TryItem(drafts[index], facts, batchOccupants, index + 1, out var item, out var status))
            {
                results[index] = status;
                ready = false;
                continue;
            }

            results[index] = PreviewRoomPatternItemStatus.Ok;
            items.Add(item);
            batchOccupants.Add(new PatternOccupant(item.SymbolRef, item.Target));
            if (!rooms.ContainsKey(item.RoomRef))
            {
                var room = facts.Rooms.First(candidate => string.Equals(candidate.RoomRef, item.RoomRef, StringComparison.Ordinal));
                rooms.Add(item.RoomRef, new PatternMepRoomBinding(
                    room.RoomRef,
                    room.Label,
                    room.LevelRef,
                    room.LevelName,
                    room.LevelElevationFeet,
                    room.Loop,
                    room.Holes,
                    room.SourceKind,
                    room.HostLevelStatus,
                    room.Link));
            }
        }

        statuses = results;
        if (!ready || rooms.Count is < 1 or > MaxRooms)
        {
            return false;
        }

        intent = new PatternMepPlacementIntent(rooms.Values.ToArray(), items);
        return true;
    }

    private static bool TryItem(
        PatternPlacementDraft draft,
        PatternProjectFacts facts,
        IReadOnlyList<PatternOccupant> batchOccupants,
        int position,
        out PatternMepPlacementItem item,
        out PreviewRoomPatternItemStatus status)
    {
        item = null!;
        status = PreviewRoomPatternItemStatus.RoomNotFound;
        if (!PatternDeviceRoles.IsRole(draft.Role) || !PatternDeviceRoles.IsPlacement(draft.PlacementKind) || draft.QuarterTurns is < 0 or > 3)
        {
            status = PreviewRoomPatternItemStatus.UnsupportedPlacement;
            return false;
        }

        if (!double.IsFinite(draft.XFeet)
            || !double.IsFinite(draft.YFeet)
            || !double.IsFinite(draft.MountingHeightFeet)
            || draft.MountingHeightFeet < 0
            || draft.MountingHeightFeet > MaxMountingFeet + 1e-9)
        {
            status = PreviewRoomPatternItemStatus.UnsupportedPlacement;
            return false;
        }

        var room = facts.Rooms.FirstOrDefault(candidate => string.Equals(candidate.RoomRef, draft.RoomRef, StringComparison.Ordinal));
        var type = facts.Types.FirstOrDefault(candidate => string.Equals(candidate.SymbolRef, draft.SymbolRef, StringComparison.Ordinal));
        if (room is null)
        {
            status = facts.RoomFailures.TryGetValue(draft.RoomRef, out var failure)
                ? failure
                : SpatialRoomRefCodec.IsLink(draft.RoomRef)
                    ? PreviewRoomPatternItemStatus.LinkedRoomUnavailable
                    : PreviewRoomPatternItemStatus.RoomNotFound;
            return false;
        }

        var linked = room.SourceKind == PatternRoomSourceKind.Link;
        if (linked && room.Link is null)
        {
            status = PreviewRoomPatternItemStatus.LinkedRoomUnavailable;
            return false;
        }

        if (type is null)
        {
            status = PreviewRoomPatternItemStatus.TypeNotFound;
            return false;
        }

        if (!CategoryAllowed(draft.Role, type.CategoryToken))
        {
            status = PreviewRoomPatternItemStatus.CategoryMismatch;
            return false;
        }

        if (!room.BoundarySupported)
        {
            status = linked
                ? PreviewRoomPatternItemStatus.LinkedBoundaryUnsupported
                : PreviewRoomPatternItemStatus.CurvedWall;
            return false;
        }

        if (!PatternBoundaryResolver.Contains(draft.XFeet, draft.YFeet, room.Loop, room.Holes, WallToleranceFeet))
        {
            status = PreviewRoomPatternItemStatus.OutsideRoom;
            return false;
        }

        var z = room.LevelElevationFeet + draft.MountingHeightFeet;
        var target = new MepPoint(draft.XFeet, draft.YFeet, z);
        if (linked && draft.PlacementKind == "boundary")
        {
            return TryLinkedBoundary(draft, room, type, target, facts.Occupants, batchOccupants, position, out item, out status);
        }

        if (IsOccupied(draft.SymbolRef, target, facts.Occupants, batchOccupants))
        {
            status = PreviewRoomPatternItemStatus.Occupied;
            return false;
        }

        PatternMepHostBinding? host = null;
        PatternPlacementStrategyKind strategy;
        var quarterTurns = draft.PlacementKind == "interior" ? draft.QuarterTurns : 0;
        MepPlanDirection direction;
        if (draft.PlacementKind == "interior")
        {
            if (linked && room.HostLevelStatus != PatternHostLevelStatus.Resolved)
            {
                status = room.HostLevelStatus == PatternHostLevelStatus.Ambiguous
                    ? PreviewRoomPatternItemStatus.HostLevelAmbiguous
                    : PreviewRoomPatternItemStatus.HostLevelNotFound;
                return false;
            }

            if (type.PlacementClassification == "one_level_based")
            {
                strategy = PatternPlacementStrategyKind.InteriorLevel;
            }
            else if (type.PlacementClassification == "work_plane_based" && Math.Abs(draft.MountingHeightFeet) <= MepDevicePlacementGeometry.OccupancyToleranceFeet)
            {
                // The instance is hosted by the Level plane and stays on it: Revit offers no writable offset and does
                // not move it off its work plane, so only a zero mounting height can be placed and verified.
                strategy = PatternPlacementStrategyKind.InteriorLevelWorkPlane;
            }
            else
            {
                status = PreviewRoomPatternItemStatus.UnsupportedPlacement;
                return false;
            }

            var angle = quarterTurns * (Math.PI / 2);
            direction = new MepPlanDirection(Math.Cos(angle), Math.Sin(angle));
        }
        else
        {
            if (!PatternBoundaryResolver.TryResolve(draft.XFeet, draft.YFeet, room.Loop, room.Walls, WallToleranceFeet, out var hit, out status))
            {
                return false;
            }

            if (type.PlacementClassification == "one_level_based_hosted")
            {
                strategy = PatternPlacementStrategyKind.BoundaryWallHosted;
            }
            else if (type.PlacementClassification == "work_plane_based" && hit.Wall.FaceResolved)
            {
                strategy = PatternPlacementStrategyKind.BoundaryWallFace;
            }
            else if (type.PlacementClassification == "work_plane_based")
            {
                status = PreviewRoomPatternItemStatus.UnsupportedFace;
                return false;
            }
            else if (type.PlacementClassification == "one_level_based")
            {
                // An unhosted level-based device stands on the Room finish line of the wall, on the Room Level.
                strategy = PatternPlacementStrategyKind.BoundaryLevel;
                var onEdge = hit.OnEdge(draft.XFeet, draft.YFeet);
                target = new MepPoint(onEdge.X, onEdge.Y, z);
                if (IsOccupied(draft.SymbolRef, target, facts.Occupants, batchOccupants))
                {
                    status = PreviewRoomPatternItemStatus.Occupied;
                    return false;
                }
            }
            else
            {
                status = PreviewRoomPatternItemStatus.UnsupportedPlacement;
                return false;
            }

            host = new PatternMepHostBinding(
                hit.Wall.WallRef,
                hit.Wall.WallName,
                new MepPoint(hit.Wall.Start.X, hit.Wall.Start.Y, room.LevelElevationFeet),
                new MepPoint(hit.Wall.End.X, hit.Wall.End.Y, room.LevelElevationFeet),
                new MepPlanDirection(hit.InsideX, hit.InsideY));
            direction = new MepPlanDirection(hit.InsideX, hit.InsideY);
        }

        item = new PatternMepPlacementItem(
            position,
            room.RoomRef,
            type.SymbolRef,
            type.CategoryToken,
            type.CategoryName,
            type.FamilyName,
            type.TypeName,
            draft.Role,
            draft.PlacementKind,
            strategy,
            room.LevelRef,
            target,
            draft.MountingHeightFeet,
            quarterTurns,
            direction,
            host,
            new PatternPlanPoint(draft.XFeet, draft.YFeet));
        status = PreviewRoomPatternItemStatus.Ok;
        return true;
    }

    private static bool TryLinkedBoundary(
        PatternPlacementDraft draft,
        PatternRoomFacts room,
        PatternTypeFacts type,
        MepPoint requested,
        IReadOnlyList<PatternOccupant> existing,
        IReadOnlyList<PatternOccupant> batch,
        int position,
        out PatternMepPlacementItem item,
        out PreviewRoomPatternItemStatus status)
    {
        item = null!;
        if (!PatternBoundaryResolver.TryResolve(requested.X, requested.Y, room.Loop, room.Walls, LinkedBoundaryToleranceFeet, out var hit, out status, wallBackedOnly: true))
        {
            return false;
        }

        if (type.PlacementClassification == "one_level_based_hosted")
        {
            status = PreviewRoomPatternItemStatus.LinkedHostRequiresWorkPlaneBased;
            return false;
        }

        var levelBased = type.PlacementClassification == "one_level_based";
        if (!levelBased && type.PlacementClassification != "work_plane_based")
        {
            status = PreviewRoomPatternItemStatus.UnsupportedPlacement;
            return false;
        }

        if (levelBased && room.HostLevelStatus != PatternHostLevelStatus.Resolved)
        {
            status = room.HostLevelStatus == PatternHostLevelStatus.Ambiguous
                ? PreviewRoomPatternItemStatus.HostLevelAmbiguous
                : PreviewRoomPatternItemStatus.HostLevelNotFound;
            return false;
        }

        if (!hit.Wall.FaceResolved
            || hit.Wall.LinkedFace is not { } face
            || (face.NormalX * hit.InsideX) + (face.NormalY * hit.InsideY) < 0.5
            || Math.Abs(face.Distance(requested.X, requested.Y)) > LinkedBoundaryToleranceFeet)
        {
            status = PreviewRoomPatternItemStatus.UnsupportedFace;
            return false;
        }

        var offset = face.Distance(requested.X, requested.Y);
        var target = new MepPoint(requested.X - (face.NormalX * offset), requested.Y - (face.NormalY * offset), requested.Z);
        if (IsOccupied(type.SymbolRef, target, existing, batch))
        {
            status = PreviewRoomPatternItemStatus.Occupied;
            return false;
        }

        var inward = new MepPlanDirection(face.NormalX, face.NormalY);
        var host = new PatternMepHostBinding(
            hit.Wall.WallRef,
            hit.Wall.WallName,
            new MepPoint(hit.Wall.Start.X, hit.Wall.Start.Y, room.LevelElevationFeet),
            new MepPoint(hit.Wall.End.X, hit.Wall.End.Y, room.LevelElevationFeet),
            inward,
            room.Link!.LinkInstanceRef,
            face);
        item = new PatternMepPlacementItem(
            position,
            room.RoomRef,
            type.SymbolRef,
            type.CategoryToken,
            type.CategoryName,
            type.FamilyName,
            type.TypeName,
            draft.Role,
            draft.PlacementKind,
            levelBased ? PatternPlacementStrategyKind.BoundaryLevel : PatternPlacementStrategyKind.BoundaryLinkedWallFace,
            room.LevelRef,
            target,
            draft.MountingHeightFeet,
            0,
            inward,
            host,
            new PatternPlanPoint(draft.XFeet, draft.YFeet));
        status = PreviewRoomPatternItemStatus.Ok;
        return true;
    }

    private static bool CategoryAllowed(string role, string categoryToken)
    {
        if (!MepDeviceCategorySupport.IsSupported(categoryToken))
        {
            return false;
        }

        var preferred = PatternDeviceRoles.PreferredCategories(role);
        return preferred.Count == 0 || preferred.Contains(categoryToken, StringComparer.Ordinal);
    }

    private static bool IsOccupied(
        string symbolRef,
        MepPoint target,
        IReadOnlyList<PatternOccupant> existing,
        IReadOnlyList<PatternOccupant> batch)
    {
        foreach (var occupant in existing.Concat(batch))
        {
            if (string.Equals(occupant.SymbolRef, symbolRef, StringComparison.Ordinal)
                && target.DistanceTo(occupant.Point) <= MepDevicePlacementGeometry.OccupancyToleranceFeet)
            {
                return true;
            }
        }

        return false;
    }
}
