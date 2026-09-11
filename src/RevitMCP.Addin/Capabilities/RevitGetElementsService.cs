using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCP.Addin.Execution;
using RevitMCP.Addin.Identity;
using RevitMCP.Addin.Inspection;
using RevitMCP.Bridge;
using RevitMCP.Contracts;

namespace RevitMCP.Addin.Capabilities;

internal sealed class RevitGetElementsService : IRevitGetElementsService
{
    private readonly RevitExecutionDispatcher _dispatcher;
    private readonly BridgeInstanceMetadata _metadata;
    private readonly OpenDocumentIdentityService _identity;

    public RevitGetElementsService(
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

    public async Task<GetElementsResult> GetElementsAsync(
        GetElementsRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        GetElementsRequestValidator.Validate(request);

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
            throw new BridgeException(
                CapabilityErrorCodes.ExecutionFailed,
                "The Revit inspection could not be executed.");
        }
        catch (Exception)
        {
            throw new BridgeException(
                CapabilityErrorCodes.ExecutionFailed,
                "The Revit inspection could not be executed.");
        }
    }

    private GetElementsResult Execute(UIApplication application, GetElementsRequest request)
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

        var metadata = new ElementBasicMetadataResolver(document);
        var requestedNames = request.Projection.ParameterNames;
        var resolved = new Dictionary<string, ElementInspectionCandidate>(StringComparer.Ordinal);

        foreach (var requestedRef in request.ElementRefs)
        {
            var element = document.GetElement(requestedRef);
            if (element is null || element is ElementType)
            {
                continue;
            }

            var basic = metadata.Read(element);
            resolved[requestedRef] = new ElementInspectionCandidate
            {
                ElementRef = requestedRef,
                Name = basic.Name,
                CategoryName = basic.CategoryName,
                FamilyName = basic.FamilyName,
                TypeName = basic.TypeName,
                LevelName = basic.LevelName,
                InstanceParameters = ReadMatchingParameters(element, document, requestedNames),
                TypeParameters = ReadMatchingTypeParameters(element, document, metadata, requestedNames)
            };
        }

        return GetElementInspectionShaper.Shape(
            new GetElementsContext
            {
                InstanceId = _metadata.InstanceId,
                DocumentId = documentId
            },
            request.ElementRefs,
            resolved,
            request.Projection);
    }

    private static IReadOnlyList<ParameterInspectionCandidate> ReadMatchingTypeParameters(
        Element element,
        Document document,
        ElementBasicMetadataResolver metadata,
        IReadOnlyList<string>? requestedNames)
    {
        if (requestedNames is null)
        {
            return [];
        }

        var type = metadata.ResolveType(element);
        return type is null
            ? []
            : ReadMatchingParameters(type, document, requestedNames);
    }

    private static IReadOnlyList<ParameterInspectionCandidate> ReadMatchingParameters(
        Element element,
        Document document,
        IReadOnlyList<string>? requestedNames)
    {
        if (requestedNames is null)
        {
            return [];
        }

        var matches = new List<ParameterInspectionCandidate>();
        foreach (var parameter in element.GetOrderedParameters())
        {
            var displayName = parameter.Definition?.Name;
            if (displayName is null || !IsRequested(displayName, requestedNames))
            {
                continue;
            }

            matches.Add(new ParameterInspectionCandidate
            {
                Name = displayName,
                ValueText = RevitParameterValueReader.Read(parameter, document)
            });
        }

        return matches;
    }

    private static bool IsRequested(string displayName, IReadOnlyList<string> requestedNames)
    {
        foreach (var requested in requestedNames)
        {
            if (string.Equals(displayName, requested, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
