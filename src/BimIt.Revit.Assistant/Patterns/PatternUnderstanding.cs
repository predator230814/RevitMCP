namespace BimIt.Revit.Assistant.Patterns;

internal static class SemanticWarningCodes
{
    public const string UnknownAnchor = "unknown_anchor";

    public const string MalformedRule = "malformed_rule";

    public const string MalformedAnchor = "malformed_anchor";

    public const string IncompatiblePlacement = "incompatible_placement";

    public const string InvalidCount = "invalid_count";

    public const string UnknownArrangement = "unknown_arrangement";

    public const string UnsupportedArrangement = "unsupported_arrangement";
}

internal sealed record SemanticReport(string Status, int WarningCount, IReadOnlyList<string> Warnings)
{
    public const string Complete = "complete";

    public const string Partial = "partial";

    public const string None = "none";

    public static SemanticReport Unset(IReadOnlyList<PatternSpec>? specs)
    {
        return new SemanticReport(specs is { Count: > 0 } ? Complete : None, 0, []);
    }
}

internal static class PatternUnderstanding
{
    public static IReadOnlyList<string> Summarize(
        IReadOnlyList<PatternSpec>? specs,
        IReadOnlyList<PatternAnchor>? anchors,
        SemanticReport? semantics = null,
        IReadOnlyList<PatternSpecEvidence>? evidence = null)
    {
        var report = semantics ?? SemanticReport.Unset(specs);
        var lines = new List<string>();
        if (report.Status == SemanticReport.None)
        {
            lines.Add("Semantic relationships were not established.");
        }

        var all = (specs ?? []).Select((spec, index) => (spec, side: Side(evidence, index))).ToArray();
        var requested = all.Where(item => item.spec.Basis == PatternBasis.UserStated).ToArray();
        var observed = all.Where(item => item.spec.Basis != PatternBasis.UserStated).ToArray();
        if (requested.Length > 0)
        {
            lines.Add("User requested:");
            lines.AddRange(requested.Select(item => Describe(item.spec, item.side)));
        }

        if (observed.Length > 0)
        {
            if (requested.Length > 0)
            {
                lines.Add("Observed:");
            }

            lines.AddRange(observed.Select(item => Describe(item.spec, item.side)));
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

        return lines.Take(12).ToList();
    }

    public static string Describe(PatternSpec spec, string? sourceSide = null)
    {
        var parts = new List<string> { $"{spec.Count} {Noun(spec.Role, spec.Count)}" };
        if (spec.IsPair)
        {
            parts.Add("symmetric pair");
        }

        parts.Add(spec.Anchor switch
        {
            PatternAnchors.RoomCenter => "interior · room centre",
            PatternAnchors.Entrance => "entrance",
            PatternAnchors.OppositeBoundary => "opposite entrance",
            PatternAnchors.NearestBoundary => "nearest wall",
            PatternAnchors.Corner => "corner",
            PatternAnchors.SourceCorrespondence => sourceSide is null ? "boundary" : $"boundary · {sourceSide} wall in sketch",
            _ => spec.Placement,
        });
        if (!string.IsNullOrEmpty(spec.Group))
        {
            parts.Add("(" + spec.Group + ")");
        }

        return string.Join(" · ", parts).Replace(" · (", " (", StringComparison.Ordinal);
    }

    public static string Noun(string role, int count)
    {
        var singular = role switch
        {
            "smoke_detector" => "smoke detector",
            "data" => "data device",
            "other" => "other device",
            _ => role,
        };
        if (count == 1)
        {
            return singular;
        }

        return singular.EndsWith("ch", StringComparison.Ordinal) ? singular + "es" : singular + "s";
    }

    private static string? Side(IReadOnlyList<PatternSpecEvidence>? evidence, int index)
    {
        return evidence is not null && index < evidence.Count ? evidence[index].Side : null;
    }
}
