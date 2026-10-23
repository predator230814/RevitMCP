using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using BimIt.Revit.Addin.Audit;
using BimIt.Revit.Addin.Identity;
using BimIt.Revit.Addin.Intents;

namespace BimIt.Revit.Addin.Capabilities;

internal static class RevitPatternMepPlacementMutation
{
    internal const string TransactionName = "BIM-IT Apply Room Pattern";

    public static ApplyMutationResult Execute(
        UIApplication application,
        OpenDocumentIdentityService identity,
        string instanceId,
        PatternMepPlacementIntent intent)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(intent);
        var uiDocument = application.ActiveUIDocument;
        if (uiDocument is null || !identity.TryGet(uiDocument.Document, out var documentId))
        {
            return new ApplyMutationResult(ApplyMutationKind.Stale, AuditTransactionStatus.None);
        }

        var document = uiDocument.Document;
        if (!Current(document, identity, instanceId, documentId, intent))
        {
            return new ApplyMutationResult(ApplyMutationKind.Stale, AuditTransactionStatus.None);
        }

        return Commit(document, intent);
    }

    private static bool Current(
        Document document,
        OpenDocumentIdentityService identity,
        string instanceId,
        string documentId,
        PatternMepPlacementIntent intent)
    {
        if (!PatternMepPlacementCanonicalEncoder.TryEncode(instanceId, documentId, intent, out _, out var stored))
        {
            return false;
        }

        var drafts = intent.Items.Select(item => new PatternPlacementDraft
        {
            RoomRef = item.RoomRef,
            SymbolRef = item.SymbolRef,
            Role = item.Role,
            PlacementKind = item.PlacementKind,
            XFeet = item.Target.X,
            YFeet = item.Target.Y,
            MountingHeightFeet = item.MountingHeightFeet,
            QuarterTurns = item.QuarterTurns,
        }).ToArray();
        if (!PatternRevitFacts.TryRead(
                document,
                identity,
                documentId,
                drafts.Select(draft => draft.RoomRef).ToArray(),
                drafts.Select(draft => draft.SymbolRef).ToArray(),
                out var facts)
            || !facts.DocumentMatches
            || !PatternPlacementComposer.TryCompose(drafts, facts, out var fresh, out _)
            || fresh is null
            || !PatternMepPlacementCanonicalEncoder.TryEncode(instanceId, documentId, fresh, out _, out var live))
        {
            return false;
        }

        return string.Equals(stored, live, StringComparison.Ordinal);
    }

    private static ApplyMutationResult Commit(Document document, PatternMepPlacementIntent intent)
    {
        Transaction? transaction = null;
        var createdRefs = new List<string>(intent.Items.Count);
        try
        {
            transaction = new Transaction(document, TransactionName);
            if (transaction.Start() != TransactionStatus.Started)
            {
                return new ApplyMutationResult(ApplyMutationKind.TransactionFailed, AuditTransactionStatus.None);
            }

            var options = transaction.GetFailureHandlingOptions();
            options.SetFailuresPreprocessor(new RollbackFailures());
            options.SetClearAfterRollback(true);
            transaction.SetFailureHandlingOptions(options);
            var symbols = new Dictionary<string, FamilySymbol>(StringComparer.Ordinal);
            foreach (var item in intent.Items)
            {
                if (document.GetElement(item.SymbolRef) is not FamilySymbol symbol)
                {
                    return Rollback(transaction);
                }

                if (!symbol.IsActive)
                {
                    symbol.Activate();
                }

                symbols[item.SymbolRef] = symbol;
                if (!TryCreate(document, item, symbol, out var created) || string.IsNullOrEmpty(created.UniqueId))
                {
                    return Rollback(transaction);
                }

                createdRefs.Add(created.UniqueId);
            }

            document.Regenerate();
            if (!FaceIntoRoom(document, intent, createdRefs))
            {
                return Rollback(transaction);
            }

            document.Regenerate();
            if (!Verify(document, intent, createdRefs))
            {
                return Rollback(transaction);
            }

            if (transaction.Commit() != TransactionStatus.Committed)
            {
                return new ApplyMutationResult(ApplyMutationKind.TransactionFailed, AuditTransactionStatus.None);
            }

            return new ApplyMutationResult(ApplyMutationKind.Applied, AuditTransactionStatus.Committed, createdRefs);
        }
        catch (Exception)
        {
            if (transaction is not null && transaction.GetStatus() == TransactionStatus.Started)
            {
                return Rollback(transaction);
            }

            return createdRefs.Count > 0
                ? new ApplyMutationResult(ApplyMutationKind.CommittedUnverified, AuditTransactionStatus.Committed)
                : new ApplyMutationResult(ApplyMutationKind.TransactionFailed, AuditTransactionStatus.None);
        }
        finally
        {
            try
            {
                transaction?.Dispose();
            }
            catch (Exception)
            {
            }
        }
    }

    private static bool TryCreate(Document document, PatternMepPlacementItem item, FamilySymbol symbol, out FamilyInstance created)
    {
        created = null!;
        if (document.GetElement(item.LevelRef) is not Level level)
        {
            return false;
        }

        var point = new XYZ(item.Target.X, item.Target.Y, item.Target.Z);
        FamilyInstance? instance = item.Strategy switch
        {
            PatternPlacementStrategyKind.InteriorLevel => document.Create.NewFamilyInstance(point, symbol, level, StructuralType.NonStructural),
            PatternPlacementStrategyKind.InteriorLevelWorkPlane => CreateOnLevelPlane(document, level, point, item, symbol),
            PatternPlacementStrategyKind.BoundaryWallHosted => CreateOnWall(document, item, point, symbol, level),
            PatternPlacementStrategyKind.BoundaryWallFace => CreateOnWallFace(document, item, point, symbol),
            _ => null,
        };
        if (instance is null)
        {
            return false;
        }

        created = instance;
        if (item.Strategy == PatternPlacementStrategyKind.InteriorLevel)
        {
            MoveTo(document, created, item.Target);
        }

        return Orient(document, created, item);
    }

    private static FamilyInstance? CreateOnLevelPlane(Document document, Level level, XYZ point, PatternMepPlacementItem item, FamilySymbol symbol)
    {
        var reference = level.GetPlaneReference();
        if (reference is null)
        {
            return null;
        }

        var direction = new XYZ(item.ReferenceDirection.X, item.ReferenceDirection.Y, 0);
        if (direction.GetLength() <= 1e-9)
        {
            return null;
        }

        var created = document.Create.NewFamilyInstance(reference, point, direction, symbol);
        if (created is not null)
        {
            MoveTo(document, created, item.Target);
        }

        return created;
    }

    private static FamilyInstance? CreateOnWall(Document document, PatternMepPlacementItem item, XYZ point, FamilySymbol symbol, Level level)
    {
        if (item.Host is null || document.GetElement(item.Host.WallRef) is not Wall wall)
        {
            return null;
        }

        return document.Create.NewFamilyInstance(point, symbol, wall, level, StructuralType.NonStructural);
    }

    private static FamilyInstance? CreateOnWallFace(Document document, PatternMepPlacementItem item, XYZ point, FamilySymbol symbol)
    {
        if (item.Host is null || document.GetElement(item.Host.WallRef) is not Wall wall || !TryRoomFace(wall, item.Host.InsideNormal, out var reference))
        {
            return null;
        }

        if (!TryPointOnFace(document, reference, point, out var onFace))
        {
            return null;
        }

        var up = new XYZ(0, 0, 1);
        return document.Create.NewFamilyInstance(reference, onFace, up, symbol);
    }

    private static bool TryPointOnFace(Document document, Reference reference, XYZ point, out XYZ onFace)
    {
        onFace = point;
        if (document.GetElement(reference.ElementId)?.GetGeometryObjectFromReference(reference) is not Face face)
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
        var plan = Math.Sqrt((dx * dx) + (dy * dy));
        if (plan > PatternPlacementComposer.WallToleranceFeet
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
        Reference? exterior = null;
        Reference? interior = null;
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

    private static void MoveTo(Document document, FamilyInstance created, MepPoint target)
    {
        if (created.Location is not LocationPoint location || location.Point is null)
        {
            return;
        }

        var delta = new XYZ(target.X - location.Point.X, target.Y - location.Point.Y, target.Z - location.Point.Z);
        if (delta.GetLength() > 1e-9)
        {
            ElementTransformUtils.MoveElement(document, created.Id, delta);
        }
    }

    private static bool Orient(Document document, FamilyInstance created, PatternMepPlacementItem item)
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
                    ElementTransformUtils.RotateElement(document, created.Id, axis, delta);
                }

                return true;
            }

            var wanted = new XYZ(item.ReferenceDirection.X, item.ReferenceDirection.Y, 0);
            var facing = created.FacingOrientation;
            if (facing is null)
            {
                return false;
            }

            var planar = new XYZ(facing.X, facing.Y, 0);
            if (planar.GetLength() <= 1e-9)
            {
                return false;
            }

            if (planar.Normalize().DotProduct(wanted.Normalize()) < 0 && created.CanFlipFacing)
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

    private static bool FaceIntoRoom(Document document, PatternMepPlacementIntent intent, IReadOnlyList<string> createdRefs)
    {
        try
        {
            for (var index = 0; index < createdRefs.Count; index++)
            {
                var item = intent.Items[index];
                if (item.Strategy is not (PatternPlacementStrategyKind.BoundaryWallHosted or PatternPlacementStrategyKind.BoundaryWallFace))
                {
                    continue;
                }

                if (document.GetElement(createdRefs[index]) is not FamilyInstance created)
                {
                    return false;
                }

                var wanted = new XYZ(item.ReferenceDirection.X, item.ReferenceDirection.Y, 0);
                var facing = created.FacingOrientation;
                if (wanted.GetLength() <= 1e-9 || facing is null)
                {
                    return false;
                }

                var planar = new XYZ(facing.X, facing.Y, 0);
                if (planar.GetLength() <= 1e-9)
                {
                    return false;
                }

                if (planar.Normalize().DotProduct(wanted.Normalize()) > 0.5)
                {
                    continue;
                }

                if (!created.CanFlipFacing)
                {
                    return false;
                }

                created.flipFacing();
            }

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool Verify(Document document, PatternMepPlacementIntent intent, IReadOnlyList<string> createdRefs)
    {
        try
        {
            if (createdRefs.Count != intent.Items.Count)
            {
                return false;
            }

            for (var index = 0; index < createdRefs.Count; index++)
            {
                var item = intent.Items[index];
                if (document.GetElement(createdRefs[index]) is not FamilyInstance created
                    || created.Symbol is null
                    || !string.Equals(created.Symbol.UniqueId, item.SymbolRef, StringComparison.Ordinal)
                    || created.Category is null
                    || !string.Equals(created.Category.BuiltInCategory.ToString(), item.CategoryToken, StringComparison.Ordinal))
                {
                    return false;
                }

                if (document.GetElement(created.LevelId) is not Level level || !string.Equals(level.UniqueId, item.LevelRef, StringComparison.Ordinal))
                {
                    return false;
                }

                if (created.Location is not LocationPoint location || location.Point is null)
                {
                    return false;
                }

                var actual = new MepPoint(location.Point.X, location.Point.Y, location.Point.Z);
                if (!LocationMatches(item, actual))
                {
                    return false;
                }

                if (item.Host is not null)
                {
                    var hostId = created.Host?.UniqueId;
                    if (item.Strategy == PatternPlacementStrategyKind.BoundaryWallHosted
                        && !string.Equals(hostId, item.Host.WallRef, StringComparison.Ordinal))
                    {
                        return false;
                    }

                    var facing = created.FacingOrientation;
                    if (facing is null)
                    {
                        return false;
                    }

                    var planar = new XYZ(facing.X, facing.Y, 0);
                    var wanted = new XYZ(item.ReferenceDirection.X, item.ReferenceDirection.Y, 0);
                    if (planar.GetLength() <= 1e-9 || planar.Normalize().DotProduct(wanted.Normalize()) <= 0.5)
                    {
                        return false;
                    }
                }
            }

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool LocationMatches(PatternMepPlacementItem item, MepPoint actual)
    {
        if (item.Strategy == PatternPlacementStrategyKind.BoundaryWallHosted)
        {
            return item.Host is not null
                && MepDevicePlacementGeometry.SameHostedWallPlacement(
                    actual,
                    item.Target,
                    item.Host.Start,
                    item.Host.End,
                    PatternPlacementComposer.WallToleranceFeet);
        }

        if (item.Strategy == PatternPlacementStrategyKind.BoundaryWallFace)
        {
            return MepDevicePlacementGeometry.SameWallPlacement(
                actual,
                item.Target,
                PatternPlacementComposer.WallToleranceFeet);
        }

        return MepDevicePlacementGeometry.SamePoint(actual, item.Target);
    }

    private static ApplyMutationResult Rollback(Transaction transaction)
    {
        try
        {
            transaction.RollBack();
        }
        catch (Exception)
        {
        }

        return new ApplyMutationResult(ApplyMutationKind.TransactionFailed, AuditTransactionStatus.RolledBack);
    }

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
