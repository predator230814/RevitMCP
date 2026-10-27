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

internal sealed record PatternRule(string Role, string Anchor, double OffsetMetres, string? Group, double? Confidence, string? Basis = null);

internal static class PatternRuleBasis
{
    public const string Observed = "observed";

    public const string UserStated = "user_stated";

    public static bool IsBasis(string? value) => value is Observed or UserStated;
}

internal sealed record PatternAnchor(string Label, double X, double Y);

internal static class SemanticWarningCodes
{
    public const string UnknownAnchor = "unknown_anchor";

    public const string MalformedRule = "malformed_rule";

    public const string MalformedAnchor = "malformed_anchor";
}

internal sealed record SemanticReport(string Status, int WarningCount, IReadOnlyList<string> Warnings)
{
    public const string Complete = "complete";

    public const string Partial = "partial";

    public const string None = "none";

    public static SemanticReport Unset(IReadOnlyList<PatternRule>? rules)
    {
        return new SemanticReport(rules is { Count: > 0 } ? Complete : None, 0, []);
    }
}

internal static class PatternUnderstanding
{
    public static IReadOnlyList<string> Summarize(
        IReadOnlyList<PatternRule>? rules,
        IReadOnlyList<PatternAnchor>? anchors,
        SemanticReport? semantics = null)
    {
        var report = semantics ?? SemanticReport.Unset(rules);
        var lines = new List<string>();
        if (report.Status == SemanticReport.None)
        {
            lines.Add("Semantic relationships were not established.");
        }

        var requested = (rules ?? []).Where(rule => rule.Basis == PatternRuleBasis.UserStated).ToArray();
        var observed = (rules ?? []).Where(rule => rule.Basis != PatternRuleBasis.UserStated).ToArray();
        if (requested.Length > 0)
        {
            lines.Add("User requested:");
            AppendRules(lines, requested);
        }

        if (observed.Length > 0)
        {
            if (requested.Length > 0)
            {
                lines.Add("Observed:");
            }

            AppendRules(lines, observed);
        }

        if (report.Status == SemanticReport.Partial)
        {
            lines.Add("Some semantic relationships were not established.");
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

    private static void AppendRules(List<string> lines, IReadOnlyList<PatternRule> rules)
    {
        foreach (var rule in rules.Where(rule => rule.Anchor == PatternAnchors.PairedSymmetric))
        {
            var suffix = string.IsNullOrEmpty(rule.Group) ? "" : $" ({rule.Group})";
            lines.Add($"2 {Noun(rule.Role, 2, PatternAnchors.PairedSymmetric)} forming a symmetric pair{suffix}");
        }

        foreach (var group in rules.Where(rule => rule.Anchor != PatternAnchors.PairedSymmetric).GroupBy(rule => (rule.Anchor, rule.Role)))
        {
            lines.Add($"{group.Count()} {Noun(group.Key.Role, group.Count(), group.Key.Anchor)} {Phrase(group.Key.Anchor)}");
        }
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
