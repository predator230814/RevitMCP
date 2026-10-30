using Autodesk.Revit.DB;
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

        using var session = new RevitPatternPlacementSession(document);
        return PatternPlacementBatch.Execute(intent, session);
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

        var drafts = PatternPlacementRevalidation.Drafts(intent);
        if (!PatternRevitFacts.TryRead(
                document,
                identity,
                documentId,
                drafts.Select(draft => draft.RoomRef).ToArray(),
                drafts.Select(draft => draft.SymbolRef).ToArray(),
                out var facts)
            || !PatternPlacementRevalidation.Same(instanceId, documentId, stored, drafts, facts, out var fresh)
            || RevitPatternLinkedGeometry.UnresolvedFaces(document, fresh!).Count > 0)
        {
            return false;
        }

        return true;
    }
}
