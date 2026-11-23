using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BimIt.Revit.Addin.Execution;
using BimIt.Revit.Addin.Identity;
using BimIt.Revit.Addin.Query;
using BimIt.Revit.Bridge;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Addin.Capabilities;

internal sealed class RevitQueryElementsService : IRevitQueryElementsService
{
    private readonly RevitExecutionDispatcher _dispatcher;
    private readonly BridgeInstanceMetadata _metadata;
    private readonly OpenDocumentIdentityService _identity;
    private readonly OpenDocumentParameterIdentityService _parameterRefs;
    private readonly IQueryQuantityConverter _quantities;
    private readonly QueryPredicateEvaluator _predicates;

    public RevitQueryElementsService(
        RevitExecutionDispatcher dispatcher,
        BridgeInstanceMetadata metadata,
        OpenDocumentIdentityService identity,
        OpenDocumentParameterIdentityService parameterRefs)
        : this(dispatcher, metadata, identity, parameterRefs, new RevitQueryQuantityConverter())
    {
    }

    internal RevitQueryElementsService(
        RevitExecutionDispatcher dispatcher,
        BridgeInstanceMetadata metadata,
        OpenDocumentIdentityService identity,
        OpenDocumentParameterIdentityService parameterRefs,
        IQueryQuantityConverter quantities)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(parameterRefs);
        ArgumentNullException.ThrowIfNull(quantities);
        _dispatcher = dispatcher;
        _metadata = metadata;
        _identity = identity;
        _parameterRefs = parameterRefs;
        _quantities = quantities;
        _predicates = new QueryPredicateEvaluator(quantities);
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
        var metadata = new ElementBasicMetadataResolver(document);
        var enumerated = collector.WhereElementIsNotElementType();

        QueryElementsMatch match;
        if (!request.HasPredicates)
        {
            match = QueryElementsMatchPlan.Match(
                enumerated,
                element => QueryElementMatcher.Matches(Project(element, metadata), request.Filters),
                element => element.UniqueId,
                predicateMatch: null,
                request.Limit,
                QueryPredicateLimits.MaxPredicateCandidates);
        }
        else
        {
            var resolved = QueryPredicatePreparer.Prepare(
                request.Predicates!,
                parameterRef => _parameterRefs.TryResolve(document, parameterRef, out var binding)
                    ? binding
                    : null,
                _quantities);
            match = QueryElementsMatchPlan.Match(
                enumerated,
                element => QueryElementMatcher.Matches(Project(element, metadata), request.Filters),
                element => element.UniqueId,
                element => _predicates.MatchesAll(
                    resolved,
                    binding => RevitQueryParameterSurfaceReader.Read(document, element, metadata, binding)),
                request.Limit,
                QueryPredicateLimits.MaxPredicateCandidates);
        }

        return new QueryElementsResult
        {
            Context = new QueryElementsContext
            {
                InstanceId = _metadata.InstanceId,
                DocumentId = documentId
            },
            MatchedCount = match.MatchedCount,
            Truncated = match.Truncated,
            ElementRefs = match.ElementRefs
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

    private static QueryElementCandidate Project(Element element, ElementBasicMetadataResolver metadata)
    {
        var basic = metadata.Read(element);
        return new QueryElementCandidate
        {
            ElementRef = element.UniqueId,
            ElementName = basic.Name,
            CategoryName = basic.CategoryName,
            FamilyName = basic.FamilyName,
            TypeName = basic.TypeName,
            LevelName = basic.LevelName
        };
    }
}
