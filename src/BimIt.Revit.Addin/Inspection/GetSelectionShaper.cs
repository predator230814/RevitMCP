namespace BimIt.Revit.Addin.Inspection;

internal readonly record struct GetSelectionProjection(
    int SelectedCount,
    IReadOnlyList<string> ElementRefs,
    bool Truncated);

internal static class GetSelectionShaper
{
    public static GetSelectionProjection Shape(IEnumerable<string> resolvedRefs, int limit)
    {
        ArgumentNullException.ThrowIfNull(resolvedRefs);

        var unique = new HashSet<string>(StringComparer.Ordinal);
        foreach (var elementRef in resolvedRefs)
        {
            unique.Add(elementRef);
        }

        var ordered = new string[unique.Count];
        unique.CopyTo(ordered);
        Array.Sort(ordered, StringComparer.Ordinal);

        var returned = ordered.Length <= limit
            ? ordered
            : ordered.Take(limit).ToArray();

        return new GetSelectionProjection(ordered.Length, returned, ordered.Length > returned.Length);
    }
}
