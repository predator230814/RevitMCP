using BimIt.Revit.Bridge;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Addin.Query;

internal readonly record struct QueryElementsMatch(
    int MatchedCount,
    bool Truncated,
    IReadOnlyList<string> ElementRefs);

internal static class QueryElementsMatchPlan
{
    public static QueryElementsMatch Match<T>(
        IEnumerable<T> enumerated,
        Func<T, bool> coarseMatch,
        Func<T, string> elementRef,
        Func<T, bool>? predicateMatch,
        int limit,
        int maxPredicateCandidates)
    {
        ArgumentNullException.ThrowIfNull(enumerated);
        ArgumentNullException.ThrowIfNull(coarseMatch);
        ArgumentNullException.ThrowIfNull(elementRef);

        if (predicateMatch is null)
        {
            var matches = new List<string>();
            foreach (var item in enumerated)
            {
                if (coarseMatch(item))
                {
                    matches.Add(elementRef(item));
                }
            }

            return Finish(matches, limit);
        }

        var candidates = new List<T>();
        foreach (var item in enumerated)
        {
            if (!coarseMatch(item))
            {
                continue;
            }

            candidates.Add(item);
            if (candidates.Count > maxPredicateCandidates)
            {
                throw new BridgeException(
                    CapabilityErrorCodes.QueryCandidateLimit,
                    "The query exceeded the candidate limit.");
            }
        }

        var predicateMatches = new List<string>();
        foreach (var item in candidates)
        {
            if (predicateMatch(item))
            {
                predicateMatches.Add(elementRef(item));
            }
        }

        return Finish(predicateMatches, limit);
    }

    private static QueryElementsMatch Finish(List<string> matches, int limit)
    {
        matches.Sort(StringComparer.Ordinal);
        var limited = matches.Count <= limit
            ? (IReadOnlyList<string>)matches
            : matches.GetRange(0, limit);
        return new QueryElementsMatch(matches.Count, matches.Count > limited.Count, limited);
    }
}
