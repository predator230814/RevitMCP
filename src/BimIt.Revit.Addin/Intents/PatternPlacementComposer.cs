using BimIt.Revit.Contracts;

namespace BimIt.Revit.Addin.Intents;

internal static class PatternPlacementComposer
{
    public const int MaxRooms = 8;

    public const int MaxPlacements = 80;

    public const double MaxMountingFeet = 6.0 * MepDevicePlacementGeometry.MetresToFeet;

    public const double WallToleranceFeet = 0.15 * MepDevicePlacementGeometry.MetresToFeet;

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
                    room.Loop));
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
            status = PreviewRoomPatternItemStatus.RoomNotFound;
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

        if (!room.BoundarySupported || !PatternBoundaryResolver.Contains(draft.XFeet, draft.YFeet, room.Loop, WallToleranceFeet))
        {
            status = room.BoundarySupported
                ? PreviewRoomPatternItemStatus.OutsideRoom
                : PreviewRoomPatternItemStatus.CurvedWall;
            return false;
        }

        var z = room.LevelElevationFeet + draft.MountingHeightFeet;
        var target = new MepPoint(draft.XFeet, draft.YFeet, z);
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
            if (type.PlacementClassification == "one_level_based")
            {
                strategy = PatternPlacementStrategyKind.InteriorLevel;
            }
            else if (type.PlacementClassification == "work_plane_based")
            {
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
            host);
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
