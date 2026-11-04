using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BimIt.Revit.Addin.Execution;
using BimIt.Revit.Addin.Identity;
using BimIt.Revit.Addin.Query;
using BimIt.Revit.Bridge;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Addin.Capabilities;

internal sealed class RevitQueryFamilyTypesService : IRevitQueryFamilyTypesService
{
    private readonly RevitExecutionDispatcher _dispatcher;
    private readonly BridgeInstanceMetadata _metadata;
    private readonly OpenDocumentIdentityService _identity;
    private readonly DocumentQueryRevisionService _revisions;
    private readonly FamilyTypeCursorCodec _cursors;
    private readonly IBuiltInCategoryCatalog _categories;

    public RevitQueryFamilyTypesService(
        RevitExecutionDispatcher dispatcher,
        BridgeInstanceMetadata metadata,
        OpenDocumentIdentityService identity,
        DocumentQueryRevisionService revisions,
        FamilyTypeCursorCodec cursors,
        IBuiltInCategoryCatalog categories)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(revisions);
        ArgumentNullException.ThrowIfNull(cursors);
        ArgumentNullException.ThrowIfNull(categories);
        _dispatcher = dispatcher;
        _metadata = metadata;
        _identity = identity;
        _revisions = revisions;
        _cursors = cursors;
        _categories = categories;
    }

    public async Task<QueryFamilyTypesResult> QueryFamilyTypesAsync(
        QueryFamilyTypesRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var validated = FamilyTypeQueryValidator.Validate(request);
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
            throw new BridgeException(CapabilityErrorCodes.ExecutionFailed, "The family type query could not be read.");
        }
    }

    private QueryFamilyTypesResult Execute(UIApplication application, ValidatedFamilyTypeQuery query)
    {
        var document = application.ActiveUIDocument?.Document;
        if (document is null)
        {
            throw new BridgeException(CapabilityErrorCodes.NoActiveDocument, "No active Revit document is available.");
        }

        if (document.IsFamilyDocument)
        {
            throw new BridgeException(CapabilityErrorCodes.UnsupportedDocumentKind, "The active document is not a project document.");
        }

        var documentId = _identity.GetId(document);
        if (!string.Equals(query.DocumentId, documentId, StringComparison.Ordinal))
        {
            throw new BridgeException(
                CapabilityErrorCodes.DocumentContextChanged,
                "The supplied document_id does not match the active document.");
        }

        var revision = _revisions.Current(documentId);
        var preparation = FamilyTypeInventoryQuery.Prepare(
            query,
            _metadata.InstanceId,
            documentId,
            hasActiveDocument: true,
            isFamilyDocument: false,
            revision,
            _categories,
            _cursors);
        var candidates = ReadCandidates(document);
        return FamilyTypeInventoryQuery.Complete(preparation, query, candidates, _cursors);
    }

    private static List<FamilyTypeCandidate> ReadCandidates(Document document)
    {
        var symbols = new FilteredElementCollector(document)
            .OfClass(typeof(FamilySymbol))
            .Cast<FamilySymbol>()
            .ToList();
        var symbolIds = new HashSet<ElementId>(symbols.Select(symbol => symbol.Id));
        var counts = new Dictionary<ElementId, int>();
        foreach (var element in new FilteredElementCollector(document).WhereElementIsNotElementType())
        {
            var typeId = element.GetTypeId();
            if (typeId is null || typeId == ElementId.InvalidElementId || !symbolIds.Contains(typeId))
            {
                continue;
            }

            counts.TryGetValue(typeId, out var count);
            counts[typeId] = count + 1;
        }

        var rows = new List<FamilyTypeCandidate>(symbols.Count);
        foreach (var symbol in symbols)
        {
            counts.TryGetValue(symbol.Id, out var placed);
            rows.Add(ReadSymbol(symbol, placed));
        }

        return rows;
    }

    private static FamilyTypeCandidate ReadSymbol(FamilySymbol symbol, int placedInstanceCount)
    {
        Family family;
        try
        {
            family = symbol.Family;
        }
        catch (Exception)
        {
            throw Unreadable();
        }

        if (family is null || string.IsNullOrEmpty(symbol.UniqueId) || string.IsNullOrEmpty(family.UniqueId))
        {
            throw Unreadable();
        }

        bool inPlace;
        try
        {
            inPlace = family.IsInPlace;
        }
        catch (Exception)
        {
            throw Unreadable();
        }

        var categoryName = "";
        if (symbol.Category is not null)
        {
            categoryName = symbol.Category.Name ?? "";
        }

        return new FamilyTypeCandidate(
            symbol.UniqueId,
            family.UniqueId,
            CategoryToken(symbol.Category),
            categoryName,
            family.Name ?? string.Empty,
            symbol.Name ?? string.Empty,
            FamilyPlacementTokens.Read(() => family.FamilyPlacementType.ToString()),
            inPlace,
            placedInstanceCount);
    }

    private static string CategoryToken(Category? category)
    {
        if (category is null)
        {
            return FamilyPlacementTokens.Unknown;
        }

        try
        {
            var builtIn = category.BuiltInCategory;
            if (builtIn == BuiltInCategory.INVALID || !Enum.IsDefined(builtIn))
            {
                return FamilyPlacementTokens.Unknown;
            }

            return Enum.GetName(builtIn) ?? FamilyPlacementTokens.Unknown;
        }
        catch (Exception)
        {
            return FamilyPlacementTokens.Unknown;
        }
    }

    private static BridgeException Unreadable()
    {
        return new BridgeException(CapabilityErrorCodes.ExecutionFailed, "The family type query could not be read.");
    }
}
