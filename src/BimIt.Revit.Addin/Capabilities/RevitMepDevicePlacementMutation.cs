using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using BimIt.Revit.Addin.Audit;
using BimIt.Revit.Addin.Identity;
using BimIt.Revit.Addin.Intents;

namespace BimIt.Revit.Addin.Capabilities;

internal static class RevitMepDevicePlacementMutation
{
    internal const string TransactionName = "BIM-IT Create MEP Device Placements";

    public static ApplyMutationResult Execute(
        UIApplication application,
        OpenDocumentIdentityService identity,
        MepDevicePlacementIntent intent)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(intent);

        if (!TryPrepare(application, identity, intent, out var prepared))
        {
            return new ApplyMutationResult(ApplyMutationKind.Stale, AuditTransactionStatus.None);
        }

        return Commit(prepared);
    }

    private static bool TryPrepare(
        UIApplication application,
        OpenDocumentIdentityService identity,
        MepDevicePlacementIntent intent,
        out PreparedBatch prepared)
    {
        prepared = default;
        try
        {
            if (!TryActiveDocument(application, identity, out var document, out var documentId))
            {
                return false;
            }

            var live = RevitMepDeviceTemplateReader.Read(document, intent.TemplateElementRef, intent.LengthUnitId);
            if (!MepDevicePlacementPreviewComposer.IsCurrent(intent, live))
            {
                return false;
            }

            if (document.GetElement(intent.TemplateElementRef) is not FamilyInstance template
                || template.Symbol is not FamilySymbol symbol
                || document.GetElement(template.LevelId) is not Level level
                || !MepCreationPlan.TrySelect(intent.Strategy, out _))
            {
                return false;
            }

            Wall? wall = null;
            if (intent.Strategy == MepPlacementStrategyKind.WallHosted)
            {
                if (template.Host is not Wall hostWall)
                {
                    return false;
                }

                wall = hostWall;
            }

            prepared = new PreparedBatch(document, documentId, intent, symbol, wall, level);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static ApplyMutationResult Commit(PreparedBatch prepared)
    {
        Transaction? transaction = null;
        var createdRefs = new List<string>(prepared.Intent.Items.Count);
        try
        {
            transaction = new Transaction(prepared.Document, TransactionName);
            TransactionStatus started;
            try
            {
                started = transaction.Start();
            }
            catch (Exception)
            {
                return Publish(ref transaction, ApplyTransactionCheckpoint.Start, null, prepared, createdRefs);
            }

            if (started != TransactionStatus.Started)
            {
                return Publish(ref transaction, ApplyTransactionCheckpoint.Start, started, prepared, createdRefs);
            }

            try
            {
                var options = transaction.GetFailureHandlingOptions();
                options.SetFailuresPreprocessor(new ApplyRollbackFailuresPreprocessor());
                options.SetClearAfterRollback(true);
                transaction.SetFailureHandlingOptions(options);
            }
            catch (Exception)
            {
                return Rollback(ref transaction);
            }

            try
            {
                if (!prepared.Symbol.IsActive)
                {
                    prepared.Symbol.Activate();
                }

                foreach (var item in prepared.Intent.Items)
                {
                    if (!TryCreate(prepared, item, out var created)
                        || string.IsNullOrEmpty(created.UniqueId)
                        || !MatchOrientation(prepared.Document, created, prepared.Intent))
                    {
                        return Rollback(ref transaction);
                    }

                    createdRefs.Add(created.UniqueId);
                }
            }
            catch (Exception)
            {
                return Rollback(ref transaction);
            }

            TransactionStatus committed;
            try
            {
                committed = transaction.Commit();
            }
            catch (Exception)
            {
                return Publish(ref transaction, ApplyTransactionCheckpoint.Commit, null, prepared, createdRefs);
            }

            return Publish(ref transaction, ApplyTransactionCheckpoint.Commit, committed, prepared, createdRefs);
        }
        finally
        {
            SafeRelease(transaction);
        }
    }

    private static bool TryCreate(PreparedBatch prepared, MepDevicePlacementItem item, out FamilyInstance created)
    {
        created = null!;
        if (!MepCreationPlan.TrySelect(prepared.Intent.Strategy, out var overload))
        {
            return false;
        }

        var creation = MepCreationPlan.CreationPoint(overload, item.Target, prepared.Level.ProjectElevation);
        var point = new XYZ(creation.X, creation.Y, creation.Z);
        FamilyInstance? instance = overload switch
        {
            MepCreationOverload.LevelPoint => prepared.Document.Create.NewFamilyInstance(
                point,
                prepared.Symbol,
                prepared.Level,
                StructuralType.NonStructural),
            MepCreationOverload.WallHost => prepared.Wall is null
                ? null
                : prepared.Document.Create.NewFamilyInstance(
                    point,
                    prepared.Symbol,
                    prepared.Wall,
                    prepared.Level,
                    StructuralType.NonStructural),
            MepCreationOverload.LevelPlaneReference => TryCreateOnLevelPlane(prepared, point),
            _ => null
        };
        if (instance is null)
        {
            return false;
        }

        created = instance;
        return true;
    }

    private static FamilyInstance? TryCreateOnLevelPlane(PreparedBatch prepared, XYZ point)
    {
        if (prepared.Intent.ReferenceDirection is not { } direction)
        {
            return null;
        }

        var reference = prepared.Level.GetPlaneReference();
        if (reference is null)
        {
            return null;
        }

        var referenceDirection = new XYZ(direction.X, direction.Y, 0);
        if (referenceDirection.GetLength() <= 1e-9)
        {
            return null;
        }

        return prepared.Document.Create.NewFamilyInstance(reference, point, referenceDirection, prepared.Symbol);
    }

    private static bool MatchOrientation(Document document, FamilyInstance created, MepDevicePlacementIntent intent)
    {
        if (intent.Strategy != MepPlacementStrategyKind.WallHosted)
        {
            if (created.Location is not LocationPoint location || location.Point is null)
            {
                return false;
            }

            var delta = Math.Atan2(
                Math.Sin(intent.Orientation.RotationRadians - location.Rotation),
                Math.Cos(intent.Orientation.RotationRadians - location.Rotation));
            if (Math.Abs(delta) > MepDevicePlacementGeometry.OrientationToleranceRadians)
            {
                var axis = Line.CreateBound(location.Point, new XYZ(location.Point.X, location.Point.Y, location.Point.Z + 1));
                ElementTransformUtils.RotateElement(document, created.Id, axis, delta);
            }
        }

        if (created.FacingFlipped != intent.Orientation.FacingFlipped)
        {
            if (!created.CanFlipFacing || !created.flipFacing() || created.FacingFlipped != intent.Orientation.FacingFlipped)
            {
                return false;
            }
        }

        if (created.HandFlipped != intent.Orientation.HandFlipped)
        {
            if (!created.CanFlipHand || !created.flipHand() || created.HandFlipped != intent.Orientation.HandFlipped)
            {
                return false;
            }
        }

        return created.Location is LocationPoint placed
            && MepDevicePlacementGeometry.OrientationMatches(
                intent.Strategy,
                placed.Rotation,
                created.FacingFlipped,
                created.HandFlipped,
                intent.Orientation);
    }

    private static ApplyMutationResult Rollback(ref Transaction? transaction)
    {
        TransactionStatus? returned = null;
        try
        {
            returned = transaction!.RollBack();
        }
        catch (Exception)
        {
            returned = null;
        }

        return Publish(ref transaction, ApplyTransactionCheckpoint.Rollback, returned, default, null);
    }

    private static ApplyMutationResult Publish(
        ref Transaction? transaction,
        ApplyTransactionCheckpoint checkpoint,
        TransactionStatus? returned,
        PreparedBatch prepared,
        IReadOnlyList<string>? createdRefs)
    {
        var getStatus = ApplyObservedTransactionStatus.Unreadable;
        if (transaction is not null && TryReadStatus(transaction, out var status))
        {
            getStatus = MapStatus(status);
        }

        var returnedStatus = returned is TransactionStatus returnedValue
            ? MapStatus(returnedValue)
            : (ApplyObservedTransactionStatus?)null;
        var classification = ApplyTransactionStatusClassifier.Observe(checkpoint, getStatus, returnedStatus);
        if (classification.Result.Kind == ApplyMutationKind.Pending)
        {
            transaction = null;
            return classification.Result;
        }

        if (!classification.RequiresVerification)
        {
            return classification.Result;
        }

        return Verify(prepared, createdRefs)
            ? new ApplyMutationResult(ApplyMutationKind.Applied, AuditTransactionStatus.Committed, createdRefs)
            : new ApplyMutationResult(ApplyMutationKind.CommittedUnverified, AuditTransactionStatus.Committed);
    }

    private static bool Verify(PreparedBatch prepared, IReadOnlyList<string>? createdRefs)
    {
        try
        {
            if (createdRefs is null || createdRefs.Count != prepared.Intent.Items.Count)
            {
                return false;
            }

            for (var index = 0; index < createdRefs.Count; index++)
            {
                if (prepared.Document.GetElement(createdRefs[index]) is not FamilyInstance created)
                {
                    return false;
                }

                if (created.Symbol is null
                    || !string.Equals(created.Symbol.UniqueId, prepared.Intent.SymbolElementRef, StringComparison.Ordinal)
                    || created.Category is null
                    || !string.Equals(created.Category.BuiltInCategory.ToString(), prepared.Intent.CategoryToken, StringComparison.Ordinal))
                {
                    return false;
                }

                var level = prepared.Document.GetElement(created.LevelId) as Level;
                if (level is null || !string.Equals(level.UniqueId, prepared.Intent.LevelElementRef, StringComparison.Ordinal))
                {
                    return false;
                }

                if (!StrategyStillMatches(prepared, created, level))
                {
                    return false;
                }

                if (created.Location is not LocationPoint location || location.Point is null)
                {
                    return false;
                }

                var actual = new MepPoint(location.Point.X, location.Point.Y, location.Point.Z);
                if (!MepDevicePlacementGeometry.SamePoint(actual, prepared.Intent.Items[index].Target)
                    || !MepDevicePlacementGeometry.OrientationMatches(
                        prepared.Intent.Strategy,
                        location.Rotation,
                        created.FacingFlipped,
                        created.HandFlipped,
                        prepared.Intent.Orientation))
                {
                    return false;
                }
            }

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool TryActiveDocument(
        UIApplication application,
        OpenDocumentIdentityService identity,
        out Document document,
        out string documentId)
    {
        document = null!;
        documentId = string.Empty;
        var uiDocument = application.ActiveUIDocument;
        if (uiDocument is null)
        {
            return false;
        }

        document = uiDocument.Document;
        if (document.IsFamilyDocument || document.IsReadOnly || !identity.TryGet(document, out documentId))
        {
            return false;
        }

        return true;
    }

    private static ApplyObservedTransactionStatus MapStatus(TransactionStatus status)
    {
        return status switch
        {
            TransactionStatus.Pending => ApplyObservedTransactionStatus.Pending,
            TransactionStatus.RolledBack => ApplyObservedTransactionStatus.RolledBack,
            TransactionStatus.Committed => ApplyObservedTransactionStatus.Committed,
            _ => ApplyObservedTransactionStatus.Other
        };
    }

    private static bool TryReadStatus(Transaction transaction, out TransactionStatus status)
    {
        try
        {
            status = transaction.GetStatus();
            return true;
        }
        catch (Exception)
        {
            status = default;
            return false;
        }
    }

    private static void SafeRelease(Transaction? transaction)
    {
        if (transaction is null)
        {
            return;
        }

        try
        {
            if (transaction.GetStatus() == TransactionStatus.Pending)
            {
                return;
            }
        }
        catch (Exception)
        {
            return;
        }

        try
        {
            transaction.Dispose();
        }
        catch (Exception)
        {
        }
    }

    private static bool StrategyStillMatches(PreparedBatch prepared, FamilyInstance created, Level level)
    {
        var placement = created.Symbol?.Family?.FamilyPlacementType;
        return prepared.Intent.Strategy switch
        {
            MepPlacementStrategyKind.LevelBased => placement == FamilyPlacementType.OneLevelBased,
            MepPlacementStrategyKind.WallHosted => placement == FamilyPlacementType.OneLevelBasedHosted
                && prepared.Intent.Wall is not null
                && created.Host is not null
                && string.Equals(created.Host.UniqueId, prepared.Intent.Wall.HostElementRef, StringComparison.Ordinal),
            MepPlacementStrategyKind.WorkPlaneBasedLevel => placement == FamilyPlacementType.WorkPlaneBased
                && RevitMepDeviceTemplateReader.IsCurrentLevelWorkPlane(created, prepared.Document, level),
            _ => false
        };
    }

    private readonly record struct PreparedBatch(
        Document Document,
        string DocumentId,
        MepDevicePlacementIntent Intent,
        FamilySymbol Symbol,
        Wall? Wall,
        Level Level);

    private sealed class ApplyRollbackFailuresPreprocessor : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            var failures = failuresAccessor.GetFailureMessages();
            if (failures is null || failures.Count == 0)
            {
                return FailureProcessingResult.Continue;
            }

            return FailureProcessingResult.ProceedWithRollBack;
        }
    }
}
