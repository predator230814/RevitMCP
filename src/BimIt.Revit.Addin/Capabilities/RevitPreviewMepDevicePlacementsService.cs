using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BimIt.Revit.Addin.Execution;
using BimIt.Revit.Addin.Identity;
using BimIt.Revit.Addin.Inspection;
using BimIt.Revit.Addin.Intents;
using BimIt.Revit.Bridge;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Addin.Capabilities;

internal sealed class RevitPreviewMepDevicePlacementsService : IRevitPreviewMepDevicePlacementsService
{
    private readonly RevitExecutionDispatcher _dispatcher;
    private readonly BridgeInstanceMetadata _metadata;
    private readonly OpenDocumentIdentityService _identity;
    private readonly EphemeralWriteIntentStore _intentStore;

    public RevitPreviewMepDevicePlacementsService(
        RevitExecutionDispatcher dispatcher,
        BridgeInstanceMetadata metadata,
        OpenDocumentIdentityService identity,
        EphemeralWriteIntentStore intentStore)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(intentStore);
        _dispatcher = dispatcher;
        _metadata = metadata;
        _identity = identity;
        _intentStore = intentStore;
    }

    public async Task<PreviewMepDevicePlacementsResult> PreviewMepDevicePlacementsAsync(
        PreviewMepDevicePlacementsRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        PreviewMepDevicePlacementsRequestValidator.Validate(request);
        try
        {
            return await _dispatcher.EnqueueAsync(application => Execute(application, request), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (BridgeException)
        {
            throw;
        }
        catch (RevitExecutionException)
        {
            throw Failed();
        }
        catch (Exception)
        {
            throw Failed();
        }
    }

    private PreviewMepDevicePlacementsResult Execute(UIApplication application, PreviewMepDevicePlacementsRequest request)
    {
        var document = RequireWritableProject(application, request.DocumentId);
        var converted = ConvertPlacements(request);
        var facts = RevitMepDeviceTemplateReader.Read(document, request.TemplateElementRef, request.LengthUnitId);
        if (!MepDevicePlacementPreviewComposer.TryEvaluate(request.TemplateElementRef, facts, converted, out var template, out var items, out var intent))
        {
            throw Failed();
        }

        var context = new PreviewMepDevicePlacementsContext
        {
            InstanceId = _metadata.InstanceId,
            DocumentId = request.DocumentId
        };
        if (intent is null || template.Status != PreviewMepDeviceTemplateStatus.Ok || items.Any(item => item.Status != PreviewMepDevicePlacementItemStatus.Ok))
        {
            return new PreviewMepDevicePlacementsResult
            {
                Context = context,
                Ready = false,
                Template = template,
                Items = items
            };
        }

        var created = _intentStore.TryCreateMep(_metadata.InstanceId, request.DocumentId, intent);
        if (created.Status == IntentStoreCreateStatus.Created
            && created.IntentRef is not null
            && created.IntentFingerprint is not null
            && created.ExpiresAt is not null)
        {
            return new PreviewMepDevicePlacementsResult
            {
                Context = context,
                Ready = true,
                Template = template,
                Items = items,
                IntentRef = created.IntentRef,
                IntentFingerprint = created.IntentFingerprint,
                ExpiresAt = created.ExpiresAt
            };
        }

        if (created.Status == IntentStoreCreateStatus.CapacityReached)
        {
            throw new BridgeException(
                CapabilityErrorCodes.IntentCapacityReached,
                "The write-intent store is at capacity.");
        }

        throw Failed();
    }

    private Document RequireWritableProject(UIApplication application, string documentId)
    {
        var uiDocument = application.ActiveUIDocument;
        if (uiDocument is null)
        {
            throw new BridgeException(CapabilityErrorCodes.NoActiveDocument, "No active Revit document is available.");
        }

        var document = uiDocument.Document;
        if (!string.Equals(documentId, _identity.GetId(document), StringComparison.Ordinal))
        {
            throw new BridgeException(
                CapabilityErrorCodes.DocumentContextChanged,
                "The supplied document_id does not match the active document.");
        }

        if (document.IsFamilyDocument)
        {
            throw new BridgeException(CapabilityErrorCodes.UnsupportedDocumentKind, "The active document is not a project document.");
        }

        if (document.IsReadOnly)
        {
            throw new BridgeException(CapabilityErrorCodes.DocumentNotWritable, "The active document is read-only.");
        }

        return document;
    }

    private static IReadOnlyList<MepConvertedPlacement> ConvertPlacements(PreviewMepDevicePlacementsRequest request)
    {
        if (!TryUnit(request.LengthUnitId, out var unit))
        {
            throw Invalid();
        }

        var converted = new MepConvertedPlacement[request.Placements.Count];
        for (var index = 0; index < request.Placements.Count; index++)
        {
            var placement = request.Placements[index];
            double along;
            double vertical;
            try
            {
                along = UnitUtils.ConvertToInternalUnits(placement.OffsetAlongHost, unit);
                vertical = UnitUtils.ConvertToInternalUnits(placement.VerticalOffset, unit);
            }
            catch (Exception)
            {
                throw Invalid();
            }

            if (!MepDevicePlacementGeometry.WithinBounds(along, vertical))
            {
                throw Invalid();
            }

            converted[index] = new MepConvertedPlacement(placement.OffsetAlongHost, placement.VerticalOffset, along, vertical);
        }

        return converted;
    }

    private static bool TryUnit(string lengthUnitId, out ForgeTypeId unit)
    {
        unit = null!;
        try
        {
            unit = new ForgeTypeId(lengthUnitId);
        }
        catch (ArgumentException)
        {
            return false;
        }

        return !unit.Empty() && UnitUtils.IsUnit(unit) && UnitUtils.IsValidUnit(SpecTypeId.Length, unit);
    }

    private static BridgeException Invalid()
    {
        return new BridgeException(
            CapabilityErrorCodes.InvalidMepDevicePlacement,
            "The MEP device placement request is invalid.");
    }

    private static BridgeException Failed()
    {
        return new BridgeException(
            CapabilityErrorCodes.ExecutionFailed,
            "The MEP device placement preview could not be executed.");
    }
}
