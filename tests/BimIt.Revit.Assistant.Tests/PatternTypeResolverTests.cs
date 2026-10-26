using BimIt.Revit.Assistant.Patterns;
using Xunit;

namespace BimIt.Revit.Assistant.Tests;

public sealed class PatternTypeResolverTests
{
    [Fact]
    public void Unique_loaded_type_is_suggested_and_a_tie_stays_ambiguous()
    {
        var one = new LoadedProjectMepTypeResolver().Resolve(new PatternTypeQuery("receptacle", null, [Type("t0", "OST_ElectricalFixtures", "Duplex", "Standard")]));
        Assert.Equal("t0", one.SuggestedOptionKey);
        Assert.False(one.Ambiguous);

        var tied = new LoadedProjectMepTypeResolver().Resolve(new PatternTypeQuery(
            "receptacle",
            null,
            [Type("t0", "OST_ElectricalFixtures", "Duplex", "Standard"), Type("t1", "OST_ElectricalFixtures", "GFCI", "Standard")]));
        Assert.Null(tied.SuggestedOptionKey);
        Assert.True(tied.Ambiguous);
        Assert.Equal(["t0", "t1"], tied.AlternateOptionKeys);
    }

    [Fact]
    public void Family_hint_ranks_without_granting_a_tie()
    {
        var catalog = new[]
        {
            Type("t0", "OST_ElectricalFixtures", "Duplex", "Standard"),
            Type("t1", "OST_ElectricalFixtures", "GFCI", "Standard"),
        };
        var unique = new LoadedProjectMepTypeResolver().Resolve(new PatternTypeQuery("receptacle", "GFCI", catalog));
        Assert.Equal("t1", unique.SuggestedOptionKey);
        Assert.False(unique.Ambiguous);

        var stillTied = new LoadedProjectMepTypeResolver().Resolve(new PatternTypeQuery(
            "receptacle",
            "Standard",
            catalog));
        Assert.Null(stillTied.SuggestedOptionKey);
        Assert.True(stillTied.Ambiguous);
    }

    [Fact]
    public void Other_makes_no_semantic_assumption()
    {
        var result = new LoadedProjectMepTypeResolver().Resolve(new PatternTypeQuery("other", "Duplex", [Type("t0", "OST_ElectricalFixtures", "Duplex", "Standard")]));
        Assert.Null(result.SuggestedOptionKey);
        Assert.True(result.Ambiguous);
    }

    [Fact]
    public void Standards_source_precedes_the_heuristic_and_has_no_provider_dependency()
    {
        var resolver = new PatternTypeResolutionPipeline(new FixedStandard("t1"));
        var result = resolver.Resolve(new PatternTypeQuery(
            "receptacle",
            "Duplex",
            [Type("t0", "OST_ElectricalFixtures", "Duplex", "Standard"), Type("t1", "OST_ElectricalFixtures", "GFCI", "Standard")]));
        Assert.Equal("t1", result.SuggestedOptionKey);
        Assert.False(result.Ambiguous);
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "src", "BimIt.Revit.Assistant", "Patterns", "PatternTypeResolver.cs"));
        Assert.DoesNotContain("HttpClient", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GitHub", source, StringComparison.Ordinal);
        Assert.Contains("IPatternTypeStandardSource", source, StringComparison.Ordinal);
    }

    private static LoadedMepTypeOption Type(string key, string category, string family, string type)
    {
        return new LoadedMepTypeOption(key, "hidden-" + key, category, category, family, type, "one_level_based", true);
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "BimIt.Revit.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate BimIt.Revit.sln.");
    }

    private sealed class FixedStandard(string key) : IPatternTypeStandardSource
    {
        public bool TryResolve(PatternTypeQuery query, out string? optionKey)
        {
            optionKey = key;
            return true;
        }
    }
}
