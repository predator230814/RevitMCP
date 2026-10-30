using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using BimIt.Revit.Addin.Intents;

namespace BimIt.Revit.Addin.Capabilities;

internal sealed class RevitPatternPlacementSession : IPatternPlacementSession
{
    private readonly Document _document;
    private Transaction? _transaction;

    public RevitPatternPlacementSession(Document document)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
    }

    public bool IsStarted => _transaction is not null && _transaction.GetStatus() == TransactionStatus.Started;

    public bool Start()
    {
        _transaction = new Transaction(_document, RevitPatternMepPlacementMutation.TransactionName);
        if (_transaction.Start() != TransactionStatus.Started)
        {
            return false;
        }

        var options = _transaction.GetFailureHandlingOptions();
        options.SetFailuresPreprocessor(new RollbackFailures());
        options.SetClearAfterRollback(true);
        _transaction.SetFailureHandlingOptions(options);
        return true;
    }

    public bool TryActivate(string symbolRef)
    {
        if (_document.GetElement(symbolRef) is not FamilySymbol symbol)
        {
            return false;
        }

        if (!symbol.IsActive)
        {
            symbol.Activate();
        }

        return true;
    }

    public bool TryCreateInteriorLevel(PatternMepPlacementItem item, out string createdRef)
    {
        createdRef = string.Empty;
        if (!TryLevelAndSymbol(item, out var level, out var symbol))
        {
            return false;
        }

        var created = _document.Create.NewFamilyInstance(Point(item.Target), symbol, level, StructuralType.NonStructural);
        if (created is null)
        {
            return false;
        }

        Mount(created, level, item.Target);
        return Finish(created, item, out createdRef);
    }

    public bool TryCreateInteriorLevelWorkPlane(PatternMepPlacementItem item, out string createdRef)
    {
        createdRef = string.Empty;
        if (!TryLevelAndSymbol(item, out var level, out var symbol))
        {
            return false;
        }

        var reference = level.GetPlaneReference();
        var direction = new XYZ(item.ReferenceDirection.X, item.ReferenceDirection.Y, 0);
        if (reference is null || direction.GetLength() <= 1e-9)
        {
            return false;
        }

        var created = _document.Create.NewFamilyInstance(reference, Point(item.Target), direction, symbol);
        if (created is null)
        {
            return false;
        }

        MoveTo(created, item.Target);
        return Finish(created, item, out createdRef);
    }

    public bool TryCreateWallHosted(PatternMepPlacementItem item, out string createdRef)
    {
        createdRef = string.Empty;
        if (!TryLevelAndSymbol(item, out var level, out var symbol)
            || item.Host is null
            || _document.GetElement(item.Host.WallRef) is not Wall wall)
        {
            return false;
        }

        var created = _document.Create.NewFamilyInstance(Point(item.Target), symbol, wall, level, StructuralType.NonStructural);
        return created is not null && Finish(created, item, out createdRef);
    }

    public bool TryCreateWallFace(PatternMepPlacementItem item, out string createdRef)
    {
        createdRef = string.Empty;
        if (!TryLevelAndSymbol(item, out _, out var symbol)
            || item.Host is null
            || _document.GetElement(item.Host.WallRef) is not Wall wall
            || !TryRoomFace(wall, item.Host.InsideNormal, out var reference)
            || !TryPointOnFace(reference, Point(item.Target), out var onFace)
            || Normal(wall.Document, reference) is not { } normal)
        {
            return false;
        }

        var created = _document.Create.NewFamilyInstance(reference, onFace, AlongFace(normal.X, normal.Y), symbol);
        return created is not null && Finish(created, item, out createdRef);
    }

    public bool TryCreateLinkedWallFace(PatternMepPlacementItem item, out string createdRef)
    {
        createdRef = string.Empty;
        if (_document.GetElement(item.SymbolRef) is not FamilySymbol symbol
            || symbol.Family is null
            || symbol.Family.FamilyPlacementType != FamilyPlacementType.WorkPlaneBased
            || item.Host is null
            || !RevitPatternLinkedGeometry.TryResolveFace(_document, item.Host, out _, out var hostReference, out var face, out var transform)
            || !RevitPatternLinkedGeometry.TryProject(face, transform, item.Target, out var onFace))
        {
            return false;
        }

        var normal = item.Host.LinkedFace!;
        var created = _document.Create.NewFamilyInstance(hostReference, Point(onFace), AlongFace(normal.NormalX, normal.NormalY), symbol);
        return created is not null && Finish(created, item, out createdRef);
    }

    public bool TryCreateWallLevel(PatternMepPlacementItem item, out string createdRef)
    {
        createdRef = string.Empty;
        if (!TryLevelAndSymbol(item, out var level, out var symbol)
            || symbol.Family is null
            || symbol.Family.FamilyPlacementType != FamilyPlacementType.OneLevelBased
            || item.Host is null)
        {
            return false;
        }

        var created = _document.Create.NewFamilyInstance(Point(item.Target), symbol, level, StructuralType.NonStructural);
        if (created is null)
        {
            return false;
        }

        Mount(created, level, item.Target);
        return Finish(created, item, out createdRef);
    }

    public void Regenerate()
    {
        _document.Regenerate();
    }

    public bool TryFaceIntoRoom(PatternMepPlacementItem item, string createdRef)
    {
        try
        {
            if (_document.GetElement(createdRef) is not FamilyInstance created)
            {
                return false;
            }

            var wanted = new XYZ(item.ReferenceDirection.X, item.ReferenceDirection.Y, 0);
            if (wanted.GetLength() <= 1e-9)
            {
                return false;
            }

            if (PatternPlacementBatch.IsFace(item.Strategy))
            {
                // The face normal points into the Room by construction; flipFacing cannot change a work-plane normal.
                return WorkPlaneNormal(created) is { } normal && normal.DotProduct(wanted.Normalize()) > 0.5;
            }

            if (Planar(created.FacingOrientation) is not { } facing)
            {
                return false;
            }

            if (facing.DotProduct(wanted.Normalize()) > 0.5)
            {
                return true;
            }

            if (!created.CanFlipFacing)
            {
                return false;
            }

            created.flipFacing();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public bool TryRead(string createdRef, out PatternPlacedFacts placed)
    {
        placed = null!;
        if (_document.GetElement(createdRef) is not FamilyInstance created
            || created.Symbol is null
            || created.Category is null
            || !RevitMountingPoint.TryRead(_document, created, out var mounted))
        {
            return false;
        }

        string? linkRef = null;
        string? linkedElementRef = null;
        var hostFace = created.HostFace;
        if (hostFace is not null
            && hostFace.LinkedElementId != ElementId.InvalidElementId
            && _document.GetElement(hostFace.ElementId) is RevitLinkInstance link)
        {
            linkRef = link.UniqueId;
            linkedElementRef = link.GetLinkDocument()?.GetElement(hostFace.LinkedElementId)?.UniqueId;
        }

        var facing = Planar(created.FacingOrientation);
        var workPlaneNormal = WorkPlaneNormal(created);
        placed = new PatternPlacedFacts
        {
            SymbolRef = created.Symbol.UniqueId,
            CategoryToken = created.Category.BuiltInCategory.ToString(),
            // A work-plane instance on a Level plane has no LevelId; its host is the Level.
            LevelRef = (_document.GetElement(created.LevelId) as Level ?? created.Host as Level)?.UniqueId,
            Location = mounted,
            Facing = facing is null ? null : new MepPlanDirection(facing.X, facing.Y),
            WorkPlaneNormal = workPlaneNormal is null ? null : new MepPlanDirection(workPlaneNormal.X, workPlaneNormal.Y),
            HostRef = created.Host?.UniqueId,
            HostFaceLinkInstanceRef = linkRef,
            HostFaceLinkedElementRef = linkedElementRef,
        };
        return true;
    }

    public bool Commit()
    {
        return _transaction is not null && _transaction.Commit() == TransactionStatus.Committed;
    }

    public void Rollback()
    {
        _transaction?.RollBack();
    }

    public void Dispose()
    {
        try
        {
            _transaction?.Dispose();
        }
        catch (Exception)
        {
        }
    }

    private bool TryLevelAndSymbol(PatternMepPlacementItem item, out Level level, out FamilySymbol symbol)
    {
        level = null!;
        symbol = null!;
        if (_document.GetElement(item.LevelRef) is not Level foundLevel
            || _document.GetElement(item.SymbolRef) is not FamilySymbol foundSymbol)
        {
            return false;
        }

        level = foundLevel;
        symbol = foundSymbol;
        return true;
    }

    private bool Finish(FamilyInstance created, PatternMepPlacementItem item, out string createdRef)
    {
        createdRef = created.UniqueId ?? string.Empty;
        return createdRef.Length > 0 && Orient(created, item);
    }

    private bool TryPointOnFace(Reference reference, XYZ point, out XYZ onFace)
    {
        onFace = point;
        if (_document.GetElement(reference.ElementId)?.GetGeometryObjectFromReference(reference) is not Face face)
        {
            return false;
        }

        IntersectionResult? projected;
        try
        {
            projected = face.Project(point);
        }
        catch (Exception)
        {
            return false;
        }

        if (projected?.XYZPoint is not XYZ candidate)
        {
            return false;
        }

        var dx = candidate.X - point.X;
        var dy = candidate.Y - point.Y;
        if (Math.Sqrt((dx * dx) + (dy * dy)) > PatternPlacementComposer.WallToleranceFeet
            || Math.Abs(candidate.Z - point.Z) > MepDevicePlacementGeometry.OccupancyToleranceFeet)
        {
            return false;
        }

        onFace = candidate;
        return true;
    }

    private static bool TryRoomFace(Wall wall, MepPlanDirection inside, out Reference reference)
    {
        reference = null!;
        Reference? exterior;
        Reference? interior;
        try
        {
            exterior = HostObjectUtils.GetSideFaces(wall, ShellLayerType.Exterior)?.FirstOrDefault();
            interior = HostObjectUtils.GetSideFaces(wall, ShellLayerType.Interior)?.FirstOrDefault();
        }
        catch (Exception)
        {
            return false;
        }

        var exteriorNormal = Normal(wall.Document, exterior);
        var interiorNormal = Normal(wall.Document, interior);
        var wanted = new XYZ(inside.X, inside.Y, 0);
        Reference? chosen = null;
        if (exteriorNormal is not null && exteriorNormal.DotProduct(wanted) > 0.5)
        {
            chosen = exterior;
        }
        else if (interiorNormal is not null && interiorNormal.DotProduct(wanted) > 0.5)
        {
            chosen = interior;
        }

        if (chosen is null || (exteriorNormal is not null && interiorNormal is not null && exteriorNormal.DotProduct(wanted) > 0.5 && interiorNormal.DotProduct(wanted) > 0.5))
        {
            return false;
        }

        reference = chosen;
        return true;
    }

    private static XYZ? Normal(Document document, Reference? reference)
    {
        if (reference is null)
        {
            return null;
        }

        try
        {
            var host = document.GetElement(reference.ElementId);
            if (host?.GetGeometryObjectFromReference(reference) is not Face face)
            {
                return null;
            }

            var box = face.GetBoundingBox();
            var normal = face.ComputeNormal(new UV((box.Min.U + box.Max.U) / 2, (box.Min.V + box.Max.V) / 2));
            return new XYZ(normal.X, normal.Y, 0);
        }
        catch (Exception)
        {
            return null;
        }
    }

    // The mounting height of a level-based instance is its Elevation from Level, not its insertion point, which a
    // family can keep above its base. The height is set first, then the instance is moved in plan only.
    private void Mount(FamilyInstance created, Level level, MepPoint target)
    {
        Regenerate();
        if (!RevitMountingPoint.TrySetHeight(_document, created, target.Z - level.Elevation))
        {
            MoveTo(created, target);
            return;
        }

        Regenerate();
        if (created.Location is LocationPoint location && location.Point is not null)
        {
            MoveTo(created, new MepPoint(target.X, target.Y, location.Point.Z));
        }
    }

    private void MoveTo(FamilyInstance created, MepPoint target)
    {
        // A level-based instance reads the point Z as an offset above its Level, and its location is stale until the
        // document regenerates. Moving from the stale location would apply the offset twice.
        Regenerate();
        if (created.Location is not LocationPoint location || location.Point is null)
        {
            return;
        }

        var delta = new XYZ(target.X - location.Point.X, target.Y - location.Point.Y, target.Z - location.Point.Z);
        if (delta.GetLength() > 1e-9)
        {
            ElementTransformUtils.MoveElement(_document, created.Id, delta);
        }
    }

    private bool Orient(FamilyInstance created, PatternMepPlacementItem item)
    {
        try
        {
            if (item.Strategy is PatternPlacementStrategyKind.InteriorLevel or PatternPlacementStrategyKind.InteriorLevelWorkPlane)
            {
                if (created.Location is not LocationPoint location || location.Point is null)
                {
                    return false;
                }

                var desired = item.QuarterTurns * (Math.PI / 2);
                var delta = Math.Atan2(Math.Sin(desired - location.Rotation), Math.Cos(desired - location.Rotation));
                if (Math.Abs(delta) > MepDevicePlacementGeometry.OrientationToleranceRadians)
                {
                    var axis = Line.CreateBound(location.Point, new XYZ(location.Point.X, location.Point.Y, location.Point.Z + 1));
                    ElementTransformUtils.RotateElement(_document, created.Id, axis, delta);
                }

                return true;
            }

            var wanted = new XYZ(item.ReferenceDirection.X, item.ReferenceDirection.Y, 0);
            if (PatternPlacementBatch.IsFace(item.Strategy))
            {
                return true;
            }

            if (Planar(created.FacingOrientation) is not { } facing)
            {
                return false;
            }

            if (item.Strategy == PatternPlacementStrategyKind.BoundaryLevel)
            {
                // An unhosted device is turned about its insertion point until it faces into the Room.
                if (created.Location is not LocationPoint point || point.Point is null || wanted.GetLength() <= 1e-9)
                {
                    return false;
                }

                var target = wanted.Normalize();
                var turn = Math.Atan2((facing.X * target.Y) - (facing.Y * target.X), facing.DotProduct(target));
                if (Math.Abs(turn) > MepDevicePlacementGeometry.OrientationToleranceRadians)
                {
                    var axis = Line.CreateBound(point.Point, new XYZ(point.Point.X, point.Point.Y, point.Point.Z + 1));
                    ElementTransformUtils.RotateElement(_document, created.Id, axis, turn);
                }

                return true;
            }

            if (facing.DotProduct(wanted.Normalize()) < 0 && created.CanFlipFacing)
            {
                created.flipFacing();
            }

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static XYZ? Planar(XYZ? facing)
    {
        if (facing is null)
        {
            return null;
        }

        var planar = new XYZ(facing.X, facing.Y, 0);
        return planar.GetLength() <= 1e-9 ? null : planar.Normalize();
    }

    private static XYZ? WorkPlaneNormal(FamilyInstance created)
    {
        try
        {
            return Planar(created.GetTransform().BasisZ);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static XYZ AlongFace(double normalX, double normalY)
    {
        var direction = PatternPlacementBatch.FaceReferenceDirection(normalX, normalY);
        return new XYZ(direction.X, direction.Y, 0);
    }

    private static XYZ Point(MepPoint point) => new(point.X, point.Y, point.Z);

    private sealed class RollbackFailures : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            var failures = failuresAccessor.GetFailureMessages();
            return failures is null || failures.Count == 0
                ? FailureProcessingResult.Continue
                : FailureProcessingResult.ProceedWithRollBack;
        }
    }
}
