using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCP.Addin.Execution;
using RevitMCP.Addin.Identity;
using RevitMCP.Addin.Inspection;
using RevitMCP.Addin.Warnings;
using RevitMCP.Bridge;
using RevitMCP.Contracts;

namespace RevitMCP.Addin.Capabilities;

internal sealed class RevitGetWarningsService : IRevitGetWarningsService
{
    private readonly RevitExecutionDispatcher _dispatcher;
    private readonly BridgeInstanceMetadata _metadata;
    private readonly OpenDocumentIdentityService _identity;

    public RevitGetWarningsService(
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

    public async Task<GetWarningsResult> GetWarningsAsync(
        GetWarningsRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var validated = GetWarningsRequestValidator.Validate(request);

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
                "The Revit warnings request could not be executed.");
        }
        catch (Exception)
        {
            throw new BridgeException(
                CapabilityErrorCodes.ExecutionFailed,
                "The Revit warnings request could not be executed.");
        }
    }

    private GetWarningsResult Execute(UIApplication application, ValidatedGetWarningsRequest request)
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
        if (!string.Equals(request.DocumentId, documentId, StringComparison.Ordinal))
        {
            throw new BridgeException(
                CapabilityErrorCodes.DocumentContextChanged,
                "The supplied document_id does not match the active document.");
        }

        var messages = new List<SourceWarning>();
        foreach (var failure in document.GetWarnings())
        {
            messages.Add(Read(document, failure));
        }

        return WarningProjection.Project(_metadata.InstanceId, documentId, request, messages);
    }

    private static SourceWarning Read(Document document, FailureMessage failure)
    {
        var definitionId = failure.GetFailureDefinitionId();
        var failing = new List<string>();
        var additional = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var unresolved = 0;
        Collect(document, failure.GetFailingElements(), failing, seen, ref unresolved);
        Collect(document, failure.GetAdditionalElements(), additional, seen, ref unresolved);
        return new SourceWarning
        {
            FailureKey = definitionId is null ? "" : definitionId.Guid.ToString(),
            Severity = MapSeverity(failure.GetSeverity()),
            Description = failure.GetDescriptionText() ?? "",
            HasResolutions = failure.HasResolutions(),
            FailingRefs = failing,
            AdditionalRefs = additional,
            UnresolvedElementCount = unresolved
        };
    }

    private static void Collect(
        Document document,
        IEnumerable<ElementId>? ids,
        List<string> refs,
        HashSet<string> seen,
        ref int unresolved)
    {
        if (ids is null)
        {
            return;
        }

        foreach (var id in ids)
        {
            if (id is null || id == ElementId.InvalidElementId)
            {
                unresolved++;
                continue;
            }

            var element = document.GetElement(id);
            if (element is null || string.IsNullOrEmpty(element.UniqueId))
            {
                unresolved++;
                continue;
            }

            if (!seen.Add(element.UniqueId))
            {
                continue;
            }

            refs.Add(element.UniqueId);
        }
    }

    private static WarningSeverity MapSeverity(FailureSeverity severity)
    {
        return severity switch
        {
            FailureSeverity.Warning => WarningSeverity.Warning,
            FailureSeverity.Error => WarningSeverity.Error,
            FailureSeverity.DocumentCorruption => WarningSeverity.DocumentCorruption,
            _ => WarningSeverity.Other
        };
    }
}
