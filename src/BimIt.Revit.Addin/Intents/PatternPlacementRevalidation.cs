namespace BimIt.Revit.Addin.Intents;

internal static class PatternPlacementRevalidation
{
    public static IReadOnlyList<PatternPlacementDraft> Drafts(PatternMepPlacementIntent intent)
    {
        return intent.Items.Select(item => new PatternPlacementDraft
        {
            RoomRef = item.RoomRef,
            SymbolRef = item.SymbolRef,
            Role = item.Role,
            PlacementKind = item.PlacementKind,
            XFeet = item.Requested.X,
            YFeet = item.Requested.Y,
            MountingHeightFeet = item.MountingHeightFeet,
            QuarterTurns = item.QuarterTurns,
        }).ToArray();
    }

    public static bool Same(
        string instanceId,
        string documentId,
        string storedFingerprint,
        IReadOnlyList<PatternPlacementDraft> drafts,
        PatternProjectFacts facts,
        out PatternMepPlacementIntent? fresh)
    {
        fresh = null;
        if (!facts.DocumentMatches
            || !PatternPlacementComposer.TryCompose(drafts, facts, out var composed, out _)
            || composed is null
            || !PatternMepPlacementCanonicalEncoder.TryEncode(instanceId, documentId, composed, out _, out var live)
            || !string.Equals(storedFingerprint, live, StringComparison.Ordinal))
        {
            return false;
        }

        fresh = composed;
        return true;
    }
}
