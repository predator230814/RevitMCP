using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using BimIt.Revit.Addin.Identity;
using BimIt.Revit.Addin.Intents;

namespace BimIt.Revit.Addin.Capabilities;

internal static class PatternRevitFacts
{
    private const int MaxOccupancyScan = 4000;

    public static bool TryRead(
        Document document,
        OpenDocumentIdentityService identity,
        string expectedDocumentId,
        IReadOnlyCollection<string> roomRefs,
        IReadOnlyCollection<string> symbolRefs,
        out PatternProjectFacts facts)
    {
        facts = new PatternProjectFacts { DocumentMatches = false, OccupancyReadSucceeded = false };
        try
        {
            if (document.IsFamilyDocument || document.IsReadOnly || !identity.TryGet(document, out var documentId))
            {
                return true;
            }

            if (!string.Equals(documentId, expectedDocumentId, StringComparison.Ordinal))
            {
                return true;
            }

            var rooms = new List<PatternRoomFacts>();
            foreach (var roomRef in roomRefs.Distinct(StringComparer.Ordinal))
            {
                if (TryRoom(document, roomRef, out var room))
                {
                    rooms.Add(room);
                }
            }

            var types = new List<PatternTypeFacts>();
            foreach (var symbolRef in symbolRefs.Distinct(StringComparer.Ordinal))
            {
                if (TryType(document, symbolRef, out var type))
                {
                    types.Add(type);
                }
            }

            var occupancy = TryOccupants(document, types.Select(type => type.SymbolRef).ToArray(), out var occupants);
            facts = new PatternProjectFacts
            {
                DocumentMatches = true,
                OccupancyReadSucceeded = occupancy,
                Rooms = rooms,
                Types = types,
                Occupants = occupants,
            };
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool TryRoom(Document document, string roomRef, out PatternRoomFacts facts)
    {
        facts = null!;
        if (document.GetElement(roomRef) is not Room room)
        {
            return false;
        }

        if (document.GetElement(room.LevelId) is not Level level || string.IsNullOrEmpty(level.UniqueId))
        {
            return false;
        }

        var options = new SpatialElementBoundaryOptions
        {
            SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish,
        };
        var loops = room.GetBoundarySegments(options);
        if (loops is null || loops.Count != 1 || loops[0] is null || loops[0].Count is < 3 or > 32)
        {
            return false;
        }

        var loop = new List<PatternPlanPoint>();
        var walls = new List<PatternWallFacts>();
        var supported = true;
        foreach (var segment in loops[0])
        {
            var curve = segment?.GetCurve();
            if (curve is null)
            {
                supported = false;
                continue;
            }

            var start = curve.GetEndPoint(0);
            var end = curve.GetEndPoint(1);
            loop.Add(new PatternPlanPoint(start.X, start.Y));
            if (curve is not Line)
            {
                supported = false;
            }

            if (segment is not null && document.GetElement(segment.ElementId) is Wall wall && !string.IsNullOrEmpty(wall.UniqueId))
            {
                var straight = wall.Location is LocationCurve location && location.Curve is Line && curve is Line;
                walls.Add(new PatternWallFacts
                {
                    WallRef = wall.UniqueId,
                    WallName = Bound(wall.Name),
                    Start = new PatternPlanPoint(start.X, start.Y),
                    End = new PatternPlanPoint(end.X, end.Y),
                    Straight = straight,
                    FaceResolved = HasSideFace(wall),
                });
                if (!straight)
                {
                    supported = false;
                }
            }
        }

        if (loop.Count < 3)
        {
            return false;
        }

        var name = room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString();
        var number = room.get_Parameter(BuiltInParameter.ROOM_NUMBER)?.AsString();
        var label = Bound(string.IsNullOrWhiteSpace(name) ? number : string.IsNullOrWhiteSpace(number) ? name : name + " " + number);
        facts = new PatternRoomFacts
        {
            RoomRef = roomRef,
            Label = string.IsNullOrWhiteSpace(label) ? "Room" : label,
            LevelRef = level.UniqueId,
            LevelName = Bound(level.Name) ?? "Level",
            LevelElevationFeet = level.ProjectElevation,
            BoundarySupported = supported,
            Loop = loop,
            Walls = walls,
        };
        return true;
    }

    private static bool HasSideFace(Wall wall)
    {
        try
        {
            var exterior = HostObjectUtils.GetSideFaces(wall, ShellLayerType.Exterior);
            var interior = HostObjectUtils.GetSideFaces(wall, ShellLayerType.Interior);
            return (exterior?.Count ?? 0) + (interior?.Count ?? 0) > 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool TryType(Document document, string symbolRef, out PatternTypeFacts facts)
    {
        facts = null!;
        if (document.GetElement(symbolRef) is not FamilySymbol symbol || symbol.Family is null || symbol.Category is null)
        {
            return false;
        }

        string placement;
        try
        {
            placement = symbol.Family.FamilyPlacementType switch
            {
                FamilyPlacementType.OneLevelBased => "one_level_based",
                FamilyPlacementType.OneLevelBasedHosted => "one_level_based_hosted",
                FamilyPlacementType.WorkPlaneBased => "work_plane_based",
                _ => "unsupported",
            };
        }
        catch (Exception)
        {
            return false;
        }

        if (string.IsNullOrEmpty(symbol.UniqueId))
        {
            return false;
        }

        facts = new PatternTypeFacts
        {
            SymbolRef = symbol.UniqueId,
            CategoryToken = symbol.Category.BuiltInCategory.ToString(),
            CategoryName = Bound(symbol.Category.Name) ?? "Category",
            FamilyName = Bound(symbol.Family.Name) ?? "Family",
            TypeName = Bound(symbol.Name) ?? "Type",
            PlacementClassification = placement,
            Active = symbol.IsActive,
        };
        return true;
    }

    private static bool TryOccupants(Document document, IReadOnlyList<string> symbolRefs, out IReadOnlyList<PatternOccupant> occupants)
    {
        var found = new List<PatternOccupant>();
        occupants = found;
        foreach (var symbolRef in symbolRefs.Distinct(StringComparer.Ordinal))
        {
            if (document.GetElement(symbolRef) is not FamilySymbol symbol)
            {
                continue;
            }

            var scanned = 0;
            try
            {
                var filter = new FamilyInstanceFilter(document, symbol.Id);
                foreach (var element in new FilteredElementCollector(document).WherePasses(filter).OfClass(typeof(FamilyInstance)))
                {
                    scanned++;
                    if (scanned > MaxOccupancyScan)
                    {
                        return false;
                    }

                    if (element is not FamilyInstance instance
                        || instance.Location is not LocationPoint location
                        || location.Point is null
                        || string.IsNullOrEmpty(symbol.UniqueId))
                    {
                        continue;
                    }

                    found.Add(new PatternOccupant(symbol.UniqueId, new MepPoint(location.Point.X, location.Point.Y, location.Point.Z)));
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        return true;
    }

    private static string Bound(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var text = value.Trim();
        return text.Length <= 120 ? text : text[..120];
    }
}
