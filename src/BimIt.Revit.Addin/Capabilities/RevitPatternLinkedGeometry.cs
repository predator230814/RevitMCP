using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using BimIt.Revit.Addin.Intents;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Addin.Capabilities;

internal static class RevitPatternLinkedGeometry
{
    private const int MaxLoopScan = 8;

    private const int MaxSegmentScan = 64;

    private const double SameNormalTolerance = 1e-7;

    private const double SameOffsetToleranceFeet = 1e-6;

    public static IReadOnlyList<PatternHostLevel> HostLevels(Document document)
    {
        var levels = new List<PatternHostLevel>();
        foreach (var element in new FilteredElementCollector(document).OfClass(typeof(Level)))
        {
            if (element is Level level && !string.IsNullOrEmpty(level.UniqueId))
            {
                levels.Add(new PatternHostLevel(level.UniqueId, Bound(level.Name, "Level"), level.ProjectElevation));
            }
        }

        return levels;
    }

    public static PatternLinkedRoomSnapshot ReadSnapshot(Document document, string linkUniqueId, string roomUniqueId)
    {
        if (document.GetElement(linkUniqueId) is not RevitLinkInstance link)
        {
            return PatternLinkedRoomSnapshot.Unavailable(PatternLinkedRoomLookupStatus.LinkMissing);
        }

        var linked = link.GetLinkDocument();
        if (linked is null)
        {
            return PatternLinkedRoomSnapshot.Unavailable(PatternLinkedRoomLookupStatus.LinkUnloaded);
        }

        if (linked.GetElement(roomUniqueId) is not Room room
            || room.Location is not LocationPoint
            || !(room.Area > 0)
            || linked.GetElement(room.LevelId) is not Level level)
        {
            return PatternLinkedRoomSnapshot.Unavailable(PatternLinkedRoomLookupStatus.RoomMissing);
        }

        var truncated = false;
        var loops = new List<IReadOnlyList<PatternLinkedSegment>>();
        var boundary = room.GetBoundarySegments(new SpatialElementBoundaryOptions
        {
            SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish,
        });
        if (boundary is null || boundary.Count > MaxLoopScan)
        {
            truncated = true;
        }
        else
        {
            foreach (var loop in boundary)
            {
                if (loop is null || loop.Count > MaxSegmentScan)
                {
                    truncated = true;
                    break;
                }

                var segments = new List<PatternLinkedSegment>(loop.Count);
                foreach (var segment in loop)
                {
                    var curve = segment?.GetCurve();
                    if (curve is null)
                    {
                        truncated = true;
                        break;
                    }

                    segments.Add(new PatternLinkedSegment
                    {
                        Start = Point(curve.GetEndPoint(0)),
                        End = Point(curve.GetEndPoint(1)),
                        Straight = curve is Line,
                        Wall = linked.GetElement(segment!.ElementId) is Wall wall ? ReadWall(wall) : null,
                    });
                }

                loops.Add(segments);
            }
        }

        var name = room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString();
        var number = room.get_Parameter(BuiltInParameter.ROOM_NUMBER)?.AsString();
        var label = string.IsNullOrWhiteSpace(name) ? number : string.IsNullOrWhiteSpace(number) ? name : name + " " + number;
        return new PatternLinkedRoomSnapshot
        {
            Status = PatternLinkedRoomLookupStatus.Ok,
            LinkLabel = SpatialLinkLabel.Sanitize(link.Name),
            Transform = Convert(link.GetTotalTransform()),
            Label = Bound(label, "Room"),
            LevelName = Bound(level.Name, "Level"),
            LevelElevationFeet = level.ProjectElevation,
            BoundaryTruncated = truncated,
            Loops = loops,
        };
    }

    public static bool TryResolveFace(
        Document document,
        PatternMepHostBinding binding,
        out RevitLinkInstance link,
        out Reference hostReference,
        out Face face,
        out PatternLinkTransform transform)
    {
        link = null!;
        hostReference = null!;
        face = null!;
        transform = default;
        if (binding.LinkInstanceRef is null
            || binding.LinkedFace is not { } expected
            || document.GetElement(binding.LinkInstanceRef) is not RevitLinkInstance instance
            || instance.GetLinkDocument() is not Document linked
            || linked.GetElement(binding.WallRef) is not Wall wall)
        {
            return false;
        }

        transform = Convert(instance.GetTotalTransform());
        if (!transform.IsPlanRigid)
        {
            return false;
        }

        Reference? chosen = null;
        Face? chosenFace = null;
        foreach (var reference in SideFaces(wall, expected.Side))
        {
            if (wall.GetGeometryObjectFromReference(reference) is not PlanarFace planar)
            {
                continue;
            }

            var normal = transform.ApplyVector(planar.FaceNormal.X, planar.FaceNormal.Y, planar.FaceNormal.Z);
            var length = Math.Sqrt((normal.X * normal.X) + (normal.Y * normal.Y));
            if (!double.IsFinite(length) || length <= 1e-9)
            {
                continue;
            }

            var nx = normal.X / length;
            var ny = normal.Y / length;
            var origin = transform.Apply(planar.Origin.X, planar.Origin.Y, planar.Origin.Z);
            var offset = (origin.X * nx) + (origin.Y * ny);
            if (Math.Abs(nx - expected.NormalX) > SameNormalTolerance
                || Math.Abs(ny - expected.NormalY) > SameNormalTolerance
                || Math.Abs(offset - expected.PlaneOffset) > SameOffsetToleranceFeet)
            {
                continue;
            }

            if (chosen is not null)
            {
                return false;
            }

            chosen = reference;
            chosenFace = planar;
        }

        if (chosen is null || chosenFace is null)
        {
            return false;
        }

        var converted = chosen.CreateLinkReference(instance);
        if (converted is null)
        {
            return false;
        }

        link = instance;
        hostReference = converted;
        face = chosenFace;
        return true;
    }

    public static bool TryProject(Face face, PatternLinkTransform transform, MepPoint target, out MepPoint onFace)
    {
        onFace = target;
        var local = transform.Inverse(target);
        IntersectionResult? projected;
        try
        {
            projected = face.Project(new XYZ(local.X, local.Y, local.Z));
        }
        catch (Exception)
        {
            return false;
        }

        if (projected?.XYZPoint is not XYZ candidate)
        {
            return false;
        }

        var host = transform.Apply(candidate.X, candidate.Y, candidate.Z);
        var dx = host.X - target.X;
        var dy = host.Y - target.Y;
        if (Math.Sqrt((dx * dx) + (dy * dy)) > PatternPlacementComposer.WallToleranceFeet
            || Math.Abs(host.Z - target.Z) > MepDevicePlacementGeometry.OccupancyToleranceFeet)
        {
            return false;
        }

        onFace = host;
        return true;
    }

    public static IReadOnlyList<int> UnresolvedFaces(Document document, PatternMepPlacementIntent intent)
    {
        var failed = new List<int>();
        foreach (var item in intent.Items)
        {
            if (item.Strategy != PatternPlacementStrategyKind.BoundaryLinkedWallFace)
            {
                continue;
            }

            if (item.Host is null
                || !TryResolveFace(document, item.Host, out _, out _, out var face, out var transform)
                || !TryProject(face, transform, item.Target, out _))
            {
                failed.Add(item.Position);
            }
        }

        return failed;
    }

    public static PatternLinkTransform Convert(Transform transform)
    {
        return new PatternLinkTransform(
            transform.Origin.X,
            transform.Origin.Y,
            transform.Origin.Z,
            transform.BasisX.X,
            transform.BasisX.Y,
            transform.BasisX.Z,
            transform.BasisY.X,
            transform.BasisY.Y,
            transform.BasisY.Z,
            transform.BasisZ.X,
            transform.BasisZ.Y,
            transform.BasisZ.Z);
    }

    private static PatternLinkedWall ReadWall(Wall wall)
    {
        var faces = new List<PatternLinkedFaceCandidate>();
        foreach (var side in new[] { PatternShellSide.Interior, PatternShellSide.Exterior })
        {
            foreach (var reference in SideFaces(wall, side))
            {
                GeometryObject? geometry;
                try
                {
                    geometry = wall.GetGeometryObjectFromReference(reference);
                }
                catch (Exception)
                {
                    geometry = null;
                }

                faces.Add(geometry is PlanarFace planar
                    ? new PatternLinkedFaceCandidate(side, true, Point(planar.FaceNormal), Point(planar.Origin))
                    : new PatternLinkedFaceCandidate(side, false, default, default));
            }
        }

        return new PatternLinkedWall
        {
            WallRef = wall.UniqueId ?? string.Empty,
            WallName = Bound(wall.Name, "Wall"),
            LocationStraight = wall.Location is LocationCurve location && location.Curve is Line,
            Faces = faces,
        };
    }

    private static IReadOnlyList<Reference> SideFaces(Wall wall, PatternShellSide side)
    {
        try
        {
            var references = HostObjectUtils.GetSideFaces(
                wall,
                side == PatternShellSide.Interior ? ShellLayerType.Interior : ShellLayerType.Exterior);
            return references is null ? [] : references.Where(reference => reference is not null).ToArray();
        }
        catch (Exception)
        {
            return [];
        }
    }

    private static MepPoint Point(XYZ point) => new(point.X, point.Y, point.Z);

    private static string Bound(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var text = value.Trim();
        return text.Length <= 120 ? text : text[..120];
    }
}
