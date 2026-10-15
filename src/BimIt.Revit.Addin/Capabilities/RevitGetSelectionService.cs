using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BimIt.Revit.Addin.Execution;
using BimIt.Revit.Addin.Identity;
using BimIt.Revit.Addin.Inspection;
using BimIt.Revit.Bridge;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Addin.Capabilities;

internal sealed class RevitGetSelectionService : IRevitGetSelectionService
{
    private readonly RevitExecutionDispatcher _dispatcher;
    private readonly BridgeInstanceMetadata _metadata;
    private readonly OpenDocumentIdentityService _identity;

    public RevitGetSelectionService(
        RevitExecutionDispatcher dispatcher,
        BridgeInstanceMetadata metadata,
        OpenDocumentIdentityService identity)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(identity);
        _dispatcher = dispatcher;
        _metadata = metadata;
        _identity = identity;
    }

    public async Task<GetSelectionResult> GetSelectionAsync(
        GetSelectionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var validated = GetSelectionRequestValidator.Validate(request);

        try
        {
            return await _dispatcher.EnqueueAsync(application => Execute(application, validated), cancellationToken)
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
            throw new BridgeException(
                CapabilityErrorCodes.ExecutionFailed,
                "The Revit selection request could not be executed.");
        }
        catch (Exception)
        {
            throw new BridgeException(
                CapabilityErrorCodes.ExecutionFailed,
                "The Revit selection request could not be executed.");
        }
    }

    private GetSelectionResult Execute(UIApplication application, ValidatedGetSelectionRequest request)
    {
        var uiDocument = application.ActiveUIDocument;
        if (uiDocument is null)
        {
            throw new BridgeException(
                CapabilityErrorCodes.NoActiveDocument,
                "No active Revit document is available.");
        }

        var document = uiDocument.Document;
        var documentId = _identity.GetId(document);
        if (request.DocumentId is not null
            && !string.Equals(request.DocumentId, documentId, StringComparison.Ordinal))
        {
            throw new BridgeException(
                CapabilityErrorCodes.DocumentContextChanged,
                "The supplied document_id does not match the active document.");
        }

        var selectedIds = uiDocument.Selection.GetElementIds();
        var resolved = new List<string>(selectedIds.Count);
        foreach (var id in selectedIds)
        {
            var element = document.GetElement(id);
            if (element?.UniqueId is not string elementRef)
            {
                continue;
            }

            resolved.Add(elementRef);
        }

        var shaped = GetSelectionShaper.Shape(resolved, request.Limit);
        return new GetSelectionResult
        {
            Context = new GetSelectionContext
            {
                InstanceId = _metadata.InstanceId,
                DocumentId = documentId
            },
            SelectedCount = shaped.SelectedCount,
            Truncated = shaped.Truncated,
            ElementRefs = shaped.ElementRefs
        };
    }
}
