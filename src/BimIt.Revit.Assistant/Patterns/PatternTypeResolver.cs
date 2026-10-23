using BimIt.Revit.Contracts;

namespace BimIt.Revit.Assistant.Patterns;

internal sealed record LoadedMepTypeOption(
    string OptionKey,
    string FamilyTypeRef,
    string CategoryToken,
    string CategoryName,
    string FamilyName,
    string TypeName,
    string PlacementClassification,
    bool Active);

internal sealed record PatternTypeQuery(string Role, string? FamilyHint, IReadOnlyList<LoadedMepTypeOption> Catalog);

internal sealed record PatternTypeResolution(
    string? SuggestedOptionKey,
    IReadOnlyList<string> AlternateOptionKeys,
    bool Ambiguous,
    string? PlacementClassification);

/// <summary>
/// Reserved seam for a future project or company standard. This slice has no provider.
/// </summary>
internal interface IPatternTypeStandardSource
{
    bool TryResolve(PatternTypeQuery query, out string? optionKey);
}

internal interface IPatternTypeResolver
{
    PatternTypeResolution Resolve(PatternTypeQuery query);
}

internal sealed class PatternTypeResolutionPipeline : IPatternTypeResolver
{
    private readonly IPatternTypeStandardSource? _standards;
    private readonly LoadedProjectMepTypeResolver _heuristic = new();

    public PatternTypeResolutionPipeline(IPatternTypeStandardSource? standards = null)
    {
        _standards = standards;
    }

    public PatternTypeResolution Resolve(PatternTypeQuery query)
    {
        if (_standards is not null && _standards.TryResolve(query, out var optionKey) && OptionExists(query, optionKey))
        {
            var match = query.Catalog.First(option => string.Equals(option.OptionKey, optionKey, StringComparison.Ordinal));
            return new PatternTypeResolution(match.OptionKey, Alternates(query, match.OptionKey), false, match.PlacementClassification);
        }

        return _heuristic.Resolve(query);
    }

    private static bool OptionExists(PatternTypeQuery query, string? optionKey)
    {
        return optionKey is not null && query.Catalog.Any(option => string.Equals(option.OptionKey, optionKey, StringComparison.Ordinal));
    }

    private static IReadOnlyList<string> Alternates(PatternTypeQuery query, string selected)
    {
        return query.Catalog
            .Where(option => Compatible(query.Role, option) && !string.Equals(option.OptionKey, selected, StringComparison.Ordinal))
            .OrderBy(option => option.FamilyName, StringComparer.Ordinal)
            .ThenBy(option => option.TypeName, StringComparer.Ordinal)
            .ThenBy(option => option.OptionKey, StringComparer.Ordinal)
            .Take(8)
            .Select(option => option.OptionKey)
            .ToArray();
    }

    internal static bool Compatible(string role, LoadedMepTypeOption option)
    {
        var preferred = PatternDeviceRoles.PreferredCategories(role);
        return preferred.Count == 0 || preferred.Contains(option.CategoryToken, StringComparer.Ordinal);
    }
}

internal sealed class LoadedProjectMepTypeResolver : IPatternTypeResolver
{
    public PatternTypeResolution Resolve(PatternTypeQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (PatternDeviceRoles.PreferredCategories(query.Role).Count == 0)
        {
            var choices = query.Catalog
                .OrderBy(option => option.FamilyName, StringComparer.Ordinal)
                .ThenBy(option => option.TypeName, StringComparer.Ordinal)
                .ThenBy(option => option.OptionKey, StringComparer.Ordinal)
                .Take(8)
                .Select(option => option.OptionKey)
                .ToArray();
            return new PatternTypeResolution(null, choices, choices.Length > 0, null);
        }

        var compatible = query.Catalog.Where(option => PatternTypeResolutionPipeline.Compatible(query.Role, option)).ToArray();
        if (compatible.Length == 0)
        {
            return new PatternTypeResolution(null, [], false, null);
        }

        var ranked = compatible
            .Select(option => (Option: option, Score: Score(option, query.FamilyHint)))
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Option.FamilyName, StringComparer.Ordinal)
            .ThenBy(item => item.Option.TypeName, StringComparer.Ordinal)
            .ThenBy(item => item.Option.OptionKey, StringComparer.Ordinal)
            .ToArray();
        var alternates = ranked.Take(8).Select(item => item.Option.OptionKey).ToArray();
        if (compatible.Length == 1)
        {
            return new PatternTypeResolution(ranked[0].Option.OptionKey, [], false, ranked[0].Option.PlacementClassification);
        }

        var hint = Normalize(query.FamilyHint);
        if (hint.Length == 0)
        {
            return new PatternTypeResolution(null, alternates, true, null);
        }

        var strong = ranked.Where(item => item.Score >= 40).ToArray();
        if (strong.Length == 1)
        {
            var rest = ranked.Where(item => !string.Equals(item.Option.OptionKey, strong[0].Option.OptionKey, StringComparison.Ordinal))
                .Take(8)
                .Select(item => item.Option.OptionKey)
                .ToArray();
            return new PatternTypeResolution(strong[0].Option.OptionKey, rest, false, strong[0].Option.PlacementClassification);
        }

        return new PatternTypeResolution(null, alternates, true, null);
    }

    private static int Score(LoadedMepTypeOption option, string? hint)
    {
        var text = Normalize(hint);
        if (text.Length == 0)
        {
            return option.Active ? 1 : 0;
        }

        var family = Normalize(option.FamilyName);
        var type = Normalize(option.TypeName);
        var combined = family + " " + type;
        if (family == text || type == text || combined == text)
        {
            return 100 + (option.Active ? 1 : 0);
        }

        if (combined.Contains(text, StringComparison.Ordinal))
        {
            return 40 + (option.Active ? 1 : 0);
        }

        return option.Active ? 1 : 0;
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var chars = value.Trim().ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray();
        return new string(chars);
    }
}
