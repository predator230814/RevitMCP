using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BimIt.Revit.Addin.Execution;
using BimIt.Revit.Addin.Identity;
using BimIt.Revit.Addin.Inspection;
using BimIt.Revit.Bridge;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Addin.Capabilities;

internal sealed class RevitGetMepDeviceTypesService : IRevitGetMepDeviceTypesService
{
    private readonly RevitExecutionDispatcher _dispatcher;
    private readonly BridgeInstanceMetadata _metadata;
    private readonly OpenDocumentIdentityService _identity;

    public RevitGetMepDeviceTypesService(
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

    public async Task<GetMepDeviceTypesResult> GetMepDeviceTypesAsync(
        GetMepDeviceTypesRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var validated = GetMepDeviceTypesRequestValidator.Validate(request);
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
                "The MEP device type catalog could not be read.");
        }
    }

    private GetMepDeviceTypesResult Execute(UIApplication application, ValidatedGetMepDeviceTypesRequest request)
    {
        var document = ActiveProject(application, request.DocumentId);
        var allowed = new HashSet<string>(request.CategoryTokens, StringComparer.Ordinal);
        var matches = new List<MepDeviceTypeItem>();
        foreach (var symbol in new FilteredElementCollector(document).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>())
        {
            if (!TryItem(symbol, allowed, request.Search, out var item))
            {
                continue;
            }

            matches.Add(item);
        }

        var ordered = matches
            .OrderBy(item => item.CategoryToken, StringComparer.Ordinal)
            .ThenBy(item => item.FamilyName, StringComparer.Ordinal)
            .ThenBy(item => item.TypeName, StringComparer.Ordinal)
            .ThenBy(item => item.FamilyTypeRef, StringComparer.Ordinal)
            .ToArray();
        var truncated = ordered.Length > request.Limit;
        return new GetMepDeviceTypesResult
        {
            Context = new GetMepDeviceTypesContext
            {
                InstanceId = _metadata.InstanceId,
                DocumentId = request.DocumentId,
            },
            Truncated = truncated,
            Items = truncated ? ordered.Take(request.Limit).ToArray() : ordered,
        };
    }

    private Document ActiveProject(UIApplication application, string documentId)
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

        var current = _identity.GetId(document);
        if (!string.Equals(documentId, current, StringComparison.Ordinal))
        {
            throw new BridgeException(CapabilityErrorCodes.DocumentContextChanged, "The supplied document_id does not match the active document.");
        }

        return document;
    }

    private static bool TryItem(FamilySymbol symbol, HashSet<string> allowed, string? search, out MepDeviceTypeItem item)
    {
        item = null!;
        try
        {
            if (symbol.Category is null || symbol.Family is null || string.IsNullOrEmpty(symbol.UniqueId))
            {
                return false;
            }

            var token = symbol.Category.BuiltInCategory.ToString();
            if (!allowed.Contains(token))
            {
                return false;
            }

            var placement = symbol.Family.FamilyPlacementType switch
            {
                FamilyPlacementType.OneLevelBased => "one_level_based",
                FamilyPlacementType.OneLevelBasedHosted => "one_level_based_hosted",
                FamilyPlacementType.WorkPlaneBased => "work_plane_based",
                _ => null,
            };
            if (placement is null)
            {
                return false;
            }

            var family = symbol.FamilyName ?? symbol.Family.Name ?? string.Empty;
            var type = symbol.Name ?? string.Empty;
            if (search is not null
                && family.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0
                && type.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }

            item = new MepDeviceTypeItem
            {
                FamilyTypeRef = symbol.UniqueId,
                CategoryToken = token,
                CategoryName = symbol.Category.Name ?? token,
                FamilyName = family,
                TypeName = type,
                PlacementClassification = placement,
                Active = symbol.IsActive,
            };
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
