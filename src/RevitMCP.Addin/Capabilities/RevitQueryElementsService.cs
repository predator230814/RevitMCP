using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCP.Addin.Execution;
using RevitMCP.Addin.Identity;
using RevitMCP.Addin.Query;
using RevitMCP.Bridge;
using RevitMCP.Contracts;

namespace RevitMCP.Addin.Capabilities;

internal sealed class RevitQueryElementsService : IRevitQueryElementsService
{
    private readonly RevitExecutionDispatcher _dispatcher;
    private readonly BridgeInstanceMetadata _metadata;
    private readonly OpenDocumentIdentityService _identity;

    public RevitQueryElementsService(
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

    public async Task<QueryElementsResult> QueryElementsAsync(
        QueryElementsRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        QueryElementsRequestValidator.Validate(request);

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
                "The Revit query could not be executed.");
        }
        catch (Exception)
        {
            throw new BridgeException(
                CapabilityErrorCodes.ExecutionFailed,
                "The Revit query could not be executed.");
        }
    }

    private QueryElementsResult Execute(UIApplication application, QueryElementsRequest request)
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

        var collector = CreateCollector(document, uiDocument, request.Scope);
        var typeCache = new Dictionary<ElementId, ElementType?>();
        var levelCache = new Dictionary<ElementId, Level?>();
        var matches = new List<string>();

        foreach (var element in collector.WhereElementIsNotElementType())
        {
            var candidate = Project(document, element, typeCache, levelCache);
            if (QueryElementMatcher.Matches(candidate, request.Filters))
            {
                matches.Add(candidate.ElementRef);
            }
        }

        matches.Sort(StringComparer.Ordinal);
        var limited = matches.Count <= request.Limit
            ? matches
            : matches.GetRange(0, request.Limit);

        return new QueryElementsResult
        {
            Context = new QueryElementsContext
            {
                InstanceId = _metadata.InstanceId,
                DocumentId = documentId
            },
            MatchedCount = matches.Count,
            Truncated = matches.Count > limited.Count,
            ElementRefs = limited
        };
    }

    private static FilteredElementCollector CreateCollector(
        Document document,
        UIDocument uiDocument,
        QueryScope scope)
    {
        if (scope == QueryScope.Document)
        {
            return new FilteredElementCollector(document);
        }

        var view = uiDocument.ActiveView;
        if (view is null
            || !FilteredElementCollector.IsViewValidForElementIteration(document, view.Id))
        {
            throw new BridgeException(
                CapabilityErrorCodes.NoActiveView,
                "The active view cannot be used for element iteration.");
        }

        return new FilteredElementCollector(document, view.Id);
    }

    private static QueryElementCandidate Project(
        Document document,
        Element element,
        Dictionary<ElementId, ElementType?> typeCache,
        Dictionary<ElementId, Level?> levelCache)
    {
        var type = ResolveType(document, element.GetTypeId(), typeCache);
        var level = ResolveLevel(document, element.LevelId, levelCache);

        return new QueryElementCandidate
        {
            ElementRef = element.UniqueId,
            ElementName = NullIfEmpty(element.Name),
            CategoryName = NullIfEmpty(element.Category?.Name),
            FamilyName = NullIfEmpty(type?.FamilyName),
            TypeName = NullIfEmpty(type?.Name),
            LevelName = NullIfEmpty(level?.Name)
        };
    }

    private static ElementType? ResolveType(
        Document document,
        ElementId typeId,
        Dictionary<ElementId, ElementType?> cache)
    {
        if (typeId == ElementId.InvalidElementId)
        {
            return null;
        }

        if (cache.TryGetValue(typeId, out var cached))
        {
            return cached;
        }

        var type = document.GetElement(typeId) as ElementType;
        cache[typeId] = type;
        return type;
    }

    private static Level? ResolveLevel(
        Document document,
        ElementId levelId,
        Dictionary<ElementId, Level?> cache)
    {
        if (levelId == ElementId.InvalidElementId)
        {
            return null;
        }

        if (cache.TryGetValue(levelId, out var cached))
        {
            return cached;
        }

        var level = document.GetElement(levelId) as Level;
        cache[levelId] = level;
        return level;
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrEmpty(value) ? null : value;
}
