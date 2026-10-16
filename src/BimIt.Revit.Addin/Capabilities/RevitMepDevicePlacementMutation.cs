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
                || template.Host is not Wall wall
                || document.GetElement(template.LevelId) is not Level level)
            {
                return false;
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
                    var created = prepared.Document.Create.NewFamilyInstance(
                        new XYZ(item.Target.X, item.Target.Y, item.Target.Z),
                        prepared.Symbol,
                        prepared.Wall,
                        prepared.Level,
                        StructuralType.NonStructural);
                    if (created is null || string.IsNullOrEmpty(created.UniqueId) || !MatchOrientation(created, prepared.Intent))
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

    private static bool MatchOrientation(FamilyInstance created, MepDevicePlacementIntent intent)
    {
        if (created.FacingFlipped != intent.FacingFlipped)
        {
            if (!created.CanFlipFacing || !created.flipFacing() || created.FacingFlipped != intent.FacingFlipped)
            {
                return false;
            }
        }

        if (created.HandFlipped != intent.HandFlipped)
        {
            if (!created.CanFlipHand || !created.flipHand() || created.HandFlipped != intent.HandFlipped)
            {
                return false;
            }
        }

        return true;
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
                    || !string.Equals(created.Category.BuiltInCategory.ToString(), prepared.Intent.CategoryToken, StringComparison.Ordinal)
                    || created.Host is null
                    || !string.Equals(created.Host.UniqueId, prepared.Intent.HostElementRef, StringComparison.Ordinal))
                {
                    return false;
                }

                var level = prepared.Document.GetElement(created.LevelId) as Level;
                if (level is null || !string.Equals(level.UniqueId, prepared.Intent.LevelElementRef, StringComparison.Ordinal))
                {
                    return false;
                }

                if (created.Location is not LocationPoint location || location.Point is null)
                {
                    return false;
                }

                var actual = new MepPoint(location.Point.X, location.Point.Y, location.Point.Z);
                if (!MepDevicePlacementGeometry.SamePoint(actual, prepared.Intent.Items[index].Target)
                    || created.FacingFlipped != prepared.Intent.FacingFlipped
                    || created.HandFlipped != prepared.Intent.HandFlipped)
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

    private readonly record struct PreparedBatch(
        Document Document,
        string DocumentId,
        MepDevicePlacementIntent Intent,
        FamilySymbol Symbol,
        Wall Wall,
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
