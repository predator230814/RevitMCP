namespace BimIt.Revit.Assistant.Patterns;

internal static class PatternAnchors
{
    public const string RoomCenter = "room_center";

    public const string Entrance = "entrance";

    public const string NearestBoundary = "nearest_boundary";

    public const string OppositeBoundary = "opposite_boundary";

    public const string Corner = "corner";

    public const string PairedSymmetric = "paired_symmetric";

    public static readonly string[] All =
    [
        RoomCenter,
        Entrance,
        NearestBoundary,
        OppositeBoundary,
        Corner,
        PairedSymmetric,
    ];

    public static bool IsAnchor(string? value) => value is not null && All.Contains(value, StringComparer.Ordinal);
}

internal sealed record PatternRule(string Role, string Anchor, double OffsetMetres, string? Group, double? Confidence);

internal sealed record PatternAnchor(string Label, double X, double Y);

internal static class PatternUnderstanding
{
    public static IReadOnlyList<string> Summarize(IReadOnlyList<PatternRule>? rules, IReadOnlyList<PatternAnchor>? anchors)
    {
        var lines = new List<string>();
        foreach (var pair in (rules ?? []).Where(rule => rule.Anchor == PatternAnchors.PairedSymmetric).GroupBy(rule => (rule.Role, rule.Group ?? "")))
        {
            lines.Add($"{pair.Count()} {Noun(pair.Key.Role, pair.Count(), PatternAnchors.PairedSymmetric)} forming a symmetric pair");
        }

        foreach (var group in (rules ?? []).Where(rule => rule.Anchor != PatternAnchors.PairedSymmetric).GroupBy(rule => (rule.Anchor, rule.Role)))
        {
            lines.Add($"{group.Count()} {Noun(group.Key.Role, group.Count(), group.Key.Anchor)} {Phrase(group.Key.Anchor)}");
        }

        if (anchors is { Count: 1 })
        {
            lines.Add("Saw a visual anchor: " + anchors[0].Label);
        }
        else if (anchors is { Count: > 1 })
        {
            lines.Add($"Saw {anchors.Count} visual anchors");
        }

        return lines;
    }

    private static string Phrase(string anchor) => anchor switch
    {
        PatternAnchors.Entrance => "associated with the entrance",
        PatternAnchors.RoomCenter => "near room centre",
        PatternAnchors.OppositeBoundary => "on an opposite wall",
        PatternAnchors.NearestBoundary => "on the nearest wall",
        PatternAnchors.Corner => "near a corner",
        _ => "in the room",
    };

    private static string Noun(string role, int count, string anchor)
    {
        if (role == "luminaire" && anchor == PatternAnchors.RoomCenter)
        {
            return count == 1 ? "ceiling luminaire" : "ceiling luminaires";
        }

        if (role == "data" && anchor == PatternAnchors.OppositeBoundary)
        {
            return count == 1 ? "data/TV device" : "data/TV devices";
        }

        return count == 1 ? role : role + "s";
    }
}
