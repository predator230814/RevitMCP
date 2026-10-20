using System.Text.Json;

namespace BimIt.Revit.Assistant;

internal static class MepDevicePlacementSelectionProbe
{
    public const int Limit = 2;

    public static bool TryRead(
        JsonElement? structured,
        out string documentId,
        out int selectedCount,
        out bool truncated,
        out List<string> refs)
    {
        documentId = "";
        selectedCount = 0;
        truncated = false;
        refs = [];
        if (structured is not { ValueKind: JsonValueKind.Object } json
            || !json.TryGetProperty("context", out var context)
            || context.ValueKind != JsonValueKind.Object
            || !json.TryGetProperty("selected_count", out var countProperty)
            || !countProperty.TryGetInt32(out selectedCount)
            || selectedCount < 0
            || !json.TryGetProperty("truncated", out var truncatedProperty)
            || truncatedProperty.ValueKind is not JsonValueKind.True and not JsonValueKind.False
            || !json.TryGetProperty("element_refs", out var refsProperty)
            || refsProperty.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        if (context.TryGetProperty("document_id", out var documentProperty) && documentProperty.ValueKind == JsonValueKind.String)
        {
            documentId = documentProperty.GetString() ?? "";
        }

        truncated = truncatedProperty.GetBoolean();
        foreach (var item in refsProperty.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || item.GetString() is not string elementRef)
            {
                return false;
            }

            refs.Add(elementRef);
        }

        return true;
    }

    public static bool IsSameTemplate(
        MepDevicePlacementProposal proposal,
        string documentId,
        int selectedCount,
        bool truncated,
        IReadOnlyList<string> refs)
    {
        return selectedCount == 1
            && !truncated
            && refs.Count == 1
            && !string.IsNullOrEmpty(refs[0])
            && string.Equals(documentId, proposal.DocumentId, StringComparison.Ordinal)
            && string.Equals(refs[0], proposal.TemplateElementRef, StringComparison.Ordinal);
    }
}
