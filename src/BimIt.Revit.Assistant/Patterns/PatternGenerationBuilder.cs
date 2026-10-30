using System.Text.Json;
using BimIt.Revit.Assistant.Ipc;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Assistant.Patterns;

internal static class PatternTypeCatalog
{
    public const string ToolName = "revit_get_mep_device_types";

    public const int MaxPerRequest = 80;

    public static async Task<IReadOnlyList<LoadedMepTypeOption>> LoadAsync(
        IAssistantToolSession session,
        string instanceId,
        string? documentId,
        IEnumerable<string> roles,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(documentId))
        {
            return [];
        }

        var options = new List<LoadedMepTypeOption>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var categories in CategoryGroups(roles))
        {
            await LoadGroupAsync(session, instanceId, documentId, categories, options, seen, cancellationToken).ConfigureAwait(false);
        }

        return options;
    }

    internal static IReadOnlyList<IReadOnlyList<string>?> CategoryGroups(IEnumerable<string> roles)
    {
        var groups = new List<IReadOnlyList<string>?>();
        foreach (var role in roles.Distinct(StringComparer.Ordinal).OrderBy(role => role, StringComparer.Ordinal))
        {
            var preferred = PatternDeviceRoles.PreferredCategories(role);
            IReadOnlyList<string>? group = preferred.Count == 0 ? null : preferred.OrderBy(token => token, StringComparer.Ordinal).ToArray();
            if (!groups.Any(existing => existing is null ? group is null : group is not null && existing.SequenceEqual(group)))
            {
                groups.Add(group);
            }
        }

        return groups;
    }

    private static async Task LoadGroupAsync(
        IAssistantToolSession session,
        string instanceId,
        string documentId,
        IReadOnlyList<string>? categories,
        List<LoadedMepTypeOption> options,
        HashSet<string> seen,
        CancellationToken cancellationToken)
    {
        var arguments = new Dictionary<string, object?>
        {
            ["instance_id"] = instanceId,
            ["document_id"] = documentId,
            ["limit"] = MaxPerRequest,
        };
        if (categories is not null)
        {
            arguments["category_tokens"] = categories;
        }

        AssistantToolCall call;
        try
        {
            call = await session.CallAsync(ToolName, arguments, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return;
        }

        if (!TryRead(call, out var items, out _))
        {
            return;
        }

        var added = 0;
        foreach (var item in items)
        {
            if (!seen.Add(item.FamilyTypeRef))
            {
                continue;
            }

            added++;
            options.Add(item.ToOption("t" + options.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            if (added >= MaxPerRequest)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Searches the loaded types of the role's categories by family or type name, as the person types it in
    /// Pattern Review. The catalog request is bounded, so a large library is reached by search, not by one list.
    /// </summary>
    public static async Task<(bool Ok, IReadOnlyList<LoadedMepTypeItem> Items, bool Truncated)> SearchAsync(
        IAssistantToolSession session,
        string instanceId,
        string documentId,
        string role,
        string query,
        CancellationToken cancellationToken)
    {
        var arguments = new Dictionary<string, object?>
        {
            ["instance_id"] = instanceId,
            ["document_id"] = documentId,
            ["search"] = query,
            ["limit"] = MaxPerRequest,
        };
        var categories = PatternDeviceRoles.PreferredCategories(role);
        if (categories.Count > 0)
        {
            arguments["category_tokens"] = categories.OrderBy(token => token, StringComparer.Ordinal).ToArray();
        }

        AssistantToolCall call;
        try
        {
            call = await session.CallAsync(ToolName, arguments, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return (false, [], false);
        }

        return TryRead(call, out var items, out var truncated) ? (true, items, truncated) : (false, [], false);
    }

    private static bool TryRead(AssistantToolCall call, out IReadOnlyList<LoadedMepTypeItem> items, out bool truncated)
    {
        items = [];
        truncated = false;
        if (call.IsError || call.Structured is not System.Text.Json.JsonElement structured)
        {
            return false;
        }

        GetMepDeviceTypesResult? result;
        try
        {
            result = structured.Deserialize<GetMepDeviceTypesResult>(ContractJson.Options);
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }

        if (result?.Items is null)
        {
            return false;
        }

        var read = new List<LoadedMepTypeItem>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in result.Items)
        {
            if (item is null
                || string.IsNullOrWhiteSpace(item.FamilyTypeRef)
                || string.IsNullOrWhiteSpace(item.CategoryToken)
                || string.IsNullOrWhiteSpace(item.FamilyName)
                || string.IsNullOrWhiteSpace(item.TypeName)
                || !seen.Add(item.FamilyTypeRef))
            {
                continue;
            }

            read.Add(new LoadedMepTypeItem(
                item.FamilyTypeRef,
                item.CategoryToken,
                item.CategoryName ?? item.CategoryToken,
                item.FamilyName,
                item.TypeName,
                item.PlacementClassification ?? string.Empty,
                item.Active));
        }

        items = read;
        truncated = result.Truncated;
        return true;
    }
}

/// <summary>One loaded type read from Revit, before it receives a local option key.</summary>
internal sealed record LoadedMepTypeItem(
    string FamilyTypeRef,
    string CategoryToken,
    string CategoryName,
    string FamilyName,
    string TypeName,
    string PlacementClassification,
    bool Active)
{
    public LoadedMepTypeOption ToOption(string optionKey)
        => new(optionKey, FamilyTypeRef, CategoryToken, CategoryName, FamilyName, TypeName, PlacementClassification, Active);
}

internal static class PatternGenerationBuilder
{
    private static readonly PatternTypeResolutionPipeline Resolver = new();

    public static RoomPatternGeneration? Apply(
        Grounding grounding,
        SourceRoomPattern source,
        IReadOnlyList<LoadedMepTypeOption> catalog,
        string instanceId,
        string artifactId,
        int pageNumber)
    {
        ArgumentNullException.ThrowIfNull(grounding);
        ArgumentNullException.ThrowIfNull(source);
        if (string.IsNullOrWhiteSpace(grounding.DocumentId))
        {
            return null;
        }

        var options = new Dictionary<string, LoadedMepTypeOption>(StringComparer.Ordinal);
        foreach (var option in catalog)
        {
            options[option.OptionKey] = option;
        }

        var rooms = new List<HiddenPatternRoom>();
        for (var index = 0; index < grounding.Shown.Count; index++)
        {
            var match = grounding.Shown[index];
            rooms.Add(new HiddenPatternRoom
            {
                Index = index,
                RoomRef = match.RoomRef ?? string.Empty,
                Label = match.Label,
                Band = match.Band,
                Score = match.Score,
                RotationDegrees = match.RotationDegrees,
                Mirrored = match.Mirrored,
                AdaptationStatus = match.AdaptationStatus,
                Devices = match.Devices.Select(device => new HiddenPatternDevice
                {
                    Role = device.Role,
                    FamilyHint = device.FamilyHint,
                    PlacementKind = device.PlacementKind,
                    XMetres = device.LocalX,
                    YMetres = device.LocalY,
                    QuarterTurns = device.PlacementKind == "interior" ? match.RotationDegrees / 90 : 0,
                }).ToArray(),
            });
        }

        var generation = new RoomPatternGeneration
        {
            GenerationId = Guid.NewGuid().ToString("N"),
            InstanceId = instanceId,
            DocumentId = grounding.DocumentId,
            ArtifactId = artifactId,
            PageNumber = pageNumber,
            Source = source,
            Rooms = rooms,
            Options = options,
        };

        grounding.Review.Generation = generation.GenerationId;
        var selectable = rooms.Where(room => room.Band != "rejected" && room.RoomRef.Length > 0 && PatternAdaptationStatus.Selectable(room.AdaptationStatus));
        grounding.Review.SuggestedRooms = (grounding.RoomName is null
                ? selectable.Where(room => room.Band == "high")
                : selectable)
            .Select(room => room.Index)
            .Take(8)
            .ToList();
        grounding.Review.Roles = BuildRoles(grounding, catalog);
        return generation;
    }

    private static List<RoomPatternReview.RoleCard> BuildRoles(Grounding grounding, IReadOnlyList<LoadedMepTypeOption> catalog)
    {
        var devices = grounding.Review.Devices ?? [];
        var roles = new List<RoomPatternReview.RoleCard>();
        foreach (var role in devices.Select(device => device.Role).Where(role => role is not null).Distinct(StringComparer.Ordinal))
        {
            var sourceHint = FirstHint(grounding, role!);
            var resolution = Resolver.Resolve(new PatternTypeQuery(role!, sourceHint, catalog));
            var keys = new List<string>();
            if (resolution.SuggestedOptionKey is not null)
            {
                keys.Add(resolution.SuggestedOptionKey);
            }

            foreach (var key in resolution.AlternateOptionKeys)
            {
                if (!keys.Contains(key, StringComparer.Ordinal))
                {
                    keys.Add(key);
                }
            }

            roles.Add(new RoomPatternReview.RoleCard
            {
                Role = role,
                FamilyHint = sourceHint,
                SuggestedOption = resolution.SuggestedOptionKey,
                PlacementClassification = resolution.PlacementClassification,
                Ambiguous = resolution.Ambiguous,
                Options = keys.Take(8).Select(key =>
                {
                    var option = catalog.First(item => item.OptionKey == key);
                    return new RoomPatternReview.TypeOption
                    {
                        Key = option.OptionKey,
                        FamilyName = option.FamilyName,
                        TypeName = option.TypeName,
                        CategoryName = option.CategoryName,
                        PlacementClassification = option.PlacementClassification,
                    };
                }).ToList(),
            });
        }

        return roles;
    }

    private static string? FirstHint(Grounding grounding, string role)
    {
        foreach (var room in grounding.Shown)
        {
            var hint = room.Devices.FirstOrDefault(device => device.Role == role)?.FamilyHint;
            if (!string.IsNullOrWhiteSpace(hint))
            {
                return hint;
            }
        }

        return null;
    }
}
