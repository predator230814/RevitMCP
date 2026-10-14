using BimIt.Revit.Contracts;

namespace BimIt.Revit.Bridge.Implementation;

internal static class BridgeContextProtocol
{
    public static GetContextResult ForSelectedVersion(GetContextResult result, int version)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (BridgeProtocol.SupportsContextDocumentId(version) || result.Document?.DocumentId is null)
        {
            return result;
        }

        var document = result.Document;
        return new GetContextResult
        {
            Instance = result.Instance,
            Document = new GetContextDocument
            {
                Title = document.Title,
                Kind = document.Kind,
                IsWorkshared = document.IsWorkshared,
                IsModelInCloud = document.IsModelInCloud,
                IsReadOnly = document.IsReadOnly,
                IsModified = document.IsModified
            },
            ActiveView = result.ActiveView,
            Selection = result.Selection
        };
    }
}
