using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BimIt.Revit.Addin.Execution;
using BimIt.Revit.Addin.Identity;
using BimIt.Revit.Addin.Inspection;
using BimIt.Revit.Addin.Intents;
using BimIt.Revit.Bridge;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Addin.Capabilities;

internal sealed class RevitPreviewRoomPatternPlacementsService : IRevitPreviewRoomPatternPlacementsService
{
    private readonly RevitExecutionDispatcher _dispatcher;
    private readonly BridgeInstanceMetadata _metadata;
    private readonly OpenDocumentIdentityService _identity;
    private readonly EphemeralWriteIntentStore _intents;

    public RevitPreviewRoomPatternPlacementsService(
        RevitExecutionDispatcher dispatcher,
        BridgeInstanceMetadata metadata,
        OpenDocumentIdentityService identity,
        EphemeralWriteIntentStore intents)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(intents);
        _dispatcher = dispatcher;
        _metadata = metadata;
        _identity = identity;
        _intents = intents;
    }

    public bool SupportsLinkedRooms => true;

    public async Task<PreviewRoomPatternPlacementsResult> PreviewRoomPatternPlacementsAsync(
        PreviewRoomPatternPlacementsRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var validated = PreviewRoomPatternPlacementsRequestValidator.Validate(request);
        try
        {
            return await _dispatcher.EnqueueAsync(application => Execute(application, validated), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (BridgeException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new BridgeException(
                CapabilityErrorCodes.ExecutionFailed,
                "The room pattern placement preview could not be created.");
        }
    }

    private PreviewRoomPatternPlacementsResult Execute(UIApplication application, ValidatedPreviewRoomPatternPlacementsRequest request)
    {
        var uiDocument = application.ActiveUIDocument;
        if (uiDocument is null)
        {
            throw new BridgeException(CapabilityErrorCodes.NoActiveDocument, "No active Revit document is available.");
        }

        var document = uiDocument.Document;
        if (document.IsFamilyDocument)
        {
            throw new BridgeException(CapabilityErrorCodes.UnsupportedDocumentKind, "The active document is not a project document.");
        }

        if (document.IsReadOnly)
        {
            throw new BridgeException(CapabilityErrorCodes.DocumentNotWritable, "The active document is read-only.");
        }

        var drafts = request.Placements.Select(ToDraft).ToArray();
        if (!PatternRevitFacts.TryRead(
                document,
                _identity,
                request.DocumentId,
                drafts.Select(draft => draft.RoomRef).ToArray(),
                drafts.Select(draft => draft.SymbolRef).ToArray(),
                out var facts))
        {
            throw new BridgeException(CapabilityErrorCodes.ExecutionFailed, "The room pattern placement preview could not be created.");
        }

        if (!facts.DocumentMatches)
        {
            throw new BridgeException(CapabilityErrorCodes.DocumentContextChanged, "The supplied document_id does not match the active document.");
        }

        var ready = PatternPlacementComposer.TryCompose(drafts, facts, out var intent, out var statuses);
        if (ready && intent is not null)
        {
            var unresolved = RevitPatternLinkedGeometry.UnresolvedFaces(document, intent);
            if (unresolved.Count > 0)
            {
                var marked = statuses.ToArray();
                foreach (var position in unresolved)
                {
                    marked[position - 1] = PreviewRoomPatternItemStatus.UnsupportedFace;
                }

                statuses = marked;
                ready = false;
                intent = null;
            }
        }

        string? intentRef = null;
        string? fingerprint = null;
        DateTimeOffset? expires = null;
        if (ready && intent is not null)
        {
            var created = _intents.TryCreatePattern(_metadata.InstanceId, request.DocumentId, intent);
            if (created.Status != IntentStoreCreateStatus.Created || created.IntentRef is null || created.IntentFingerprint is null)
            {
                throw new BridgeException(
                    created.Status == IntentStoreCreateStatus.CapacityReached
                        ? CapabilityErrorCodes.IntentCapacityReached
                        : CapabilityErrorCodes.InvalidRoomPatternPlacement,
                    "The room pattern placement preview could not be stored.");
            }

            intentRef = created.IntentRef;
            fingerprint = created.IntentFingerprint;
            expires = created.ExpiresAt;
        }

        return new PreviewRoomPatternPlacementsResult
        {
            Context = new PreviewRoomPatternPlacementsContext
            {
                InstanceId = _metadata.InstanceId,
                DocumentId = request.DocumentId,
            },
            LengthUnit = PreviewRoomPatternPlacementsResult.Metres,
            Ready = ready && intentRef is not null,
            RoomCount = drafts.Select(draft => draft.RoomRef).Distinct(StringComparer.Ordinal).Count(),
            Items = statuses.Select((status, index) => new PreviewRoomPatternItemResult
            {
                Position = index + 1,
                Status = status,
            }).ToArray(),
            IntentRef = intentRef,
            IntentFingerprint = fingerprint,
            ExpiresAt = expires,
        };
    }

    private static PatternPlacementDraft ToDraft(ValidatedRoomPatternPlacement placement)
    {
        return new PatternPlacementDraft
        {
            RoomRef = placement.RoomRef,
            SymbolRef = placement.FamilyTypeRef,
            Role = placement.Role,
            PlacementKind = placement.PlacementKind,
            XFeet = UnitUtils.ConvertToInternalUnits(placement.XMetres, UnitTypeId.Meters),
            YFeet = UnitUtils.ConvertToInternalUnits(placement.YMetres, UnitTypeId.Meters),
            MountingHeightFeet = UnitUtils.ConvertToInternalUnits(placement.MountingHeightMetres, UnitTypeId.Meters),
            QuarterTurns = placement.QuarterTurns,
        };
    }
}
