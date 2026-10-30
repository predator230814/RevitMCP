using BimIt.Revit.Assistant.Ipc;

namespace BimIt.Revit.Assistant.Patterns;

internal sealed record RoomPatternTypeSearchOutcome(string Title, string Body, bool IsError, string? ResultJson);

/// <summary>
/// Pattern Review type search. The person types part of a family or type name. BIM-IT reads the loaded types
/// of the role's categories from Revit, adds them to the current generation as bounded local option keys, and
/// returns those keys for the WebView. The model is not involved and never sees the result.
/// </summary>
internal sealed class RoomPatternTypeSearchService
{
    public const string Title = "Type search";

    private readonly IAssistantToolSession _session;
    private readonly RoomPatternStore _store;
    private readonly string _instanceId;

    public RoomPatternTypeSearchService(IAssistantToolSession session, RoomPatternStore store, string instanceId)
    {
        _session = session;
        _store = store;
        _instanceId = instanceId;
    }

    public async Task<RoomPatternTypeSearchOutcome> SearchAsync(string? request, CancellationToken cancellationToken)
    {
        if (!RoomPatternTypeSearch.TryParseRequest(request, out var role, out var query))
        {
            return Failure("The type search is not valid.");
        }

        var generation = _store.CurrentGeneration;
        if (generation is null || _store.HasTerminalApply)
        {
            return Failure("There is no pattern to configure.");
        }

        var (ok, items, truncated) = await PatternTypeCatalog.SearchAsync(
            _session,
            _instanceId,
            generation.DocumentId,
            role,
            query,
            cancellationToken).ConfigureAwait(false);
        if (!ok)
        {
            return Failure("The loaded types could not be read from Revit.");
        }

        if (!_store.TryAddOptions(generation.GenerationId, items, out var added))
        {
            return Failure("The pattern changed. Search again.");
        }

        var options = added
            .Where(option => PatternTypeResolutionPipeline.Compatible(role, option))
            .OrderBy(option => option.FamilyName, StringComparer.Ordinal)
            .ThenBy(option => option.TypeName, StringComparer.Ordinal)
            .ThenBy(option => option.OptionKey, StringComparer.Ordinal)
            .Take(RoomPatternTypeSearch.MaxOptions)
            .Select(option => new RoomPatternReview.TypeOption
            {
                Key = option.OptionKey,
                FamilyName = Label(option.FamilyName),
                TypeName = Label(option.TypeName),
                CategoryName = Label(option.CategoryName),
                PlacementClassification = option.PlacementClassification,
            })
            .ToList();
        var json = RoomPatternTypeSearch.SerializeResult(new RoomPatternTypeSearch.Result
        {
            Generation = generation.GenerationId,
            Role = role,
            Query = query,
            Truncated = truncated || added.Count < items.Count,
            Options = options,
        });
        if (json is null)
        {
            return Failure("The loaded types could not be shown.");
        }

        var body = options.Count switch
        {
            0 => $"No loaded {role} type matches \"{query}\".",
            1 => $"1 loaded type matches \"{query}\".",
            _ => $"{options.Count} loaded types match \"{query}\".",
        };
        if (truncated)
        {
            body += " More types match. Refine the search.";
        }

        return new RoomPatternTypeSearchOutcome(Title, body, false, json);
    }

    private static RoomPatternTypeSearchOutcome Failure(string body) => new(Title, body, true, null);

    private static string Label(string value)
    {
        var clean = new string(value.Where(character => !char.IsControl(character)).ToArray());
        return clean.Length <= 120 ? clean : clean[..120];
    }
}
