using System.Text.Json;
using BimIt.Revit.Assistant.Ipc;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Assistant.Patterns;

internal static class PatternTypeCatalog
{
    public const string ToolName = "revit_get_mep_device_types";

    public static async Task<IReadOnlyList<LoadedMepTypeOption>> LoadAsync(
        IAssistantToolSession session,
        string instanceId,
        string? documentId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(documentId))
        {
            return [];
        }

        AssistantToolCall call;
        try
        {
            call = await session.CallAsync(
                ToolName,
                new Dictionary<string, object?>
                {
                    ["instance_id"] = instanceId,
                    ["document_id"] = documentId,
                    ["limit"] = 80,
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return [];
        }

        if (call.IsError || call.Structured is not System.Text.Json.JsonElement structured)
        {
            return [];
        }

        GetMepDeviceTypesResult? result;
        try
        {
            result = structured.Deserialize<GetMepDeviceTypesResult>(ContractJson.Options);
        }
        catch (System.Text.Json.JsonException)
        {
            return [];
        }

        if (result?.Items is null)
        {
            return [];
        }

        var options = new List<LoadedMepTypeOption>();
        foreach (var item in result.Items)
        {
            if (item is null
                || string.IsNullOrWhiteSpace(item.FamilyTypeRef)
                || string.IsNullOrWhiteSpace(item.CategoryToken)
                || string.IsNullOrWhiteSpace(item.FamilyName)
                || string.IsNullOrWhiteSpace(item.TypeName))
            {
                continue;
            }

            options.Add(new LoadedMepTypeOption(
                "t" + options.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                item.FamilyTypeRef,
                item.CategoryToken,
                item.CategoryName ?? item.CategoryToken,
                item.FamilyName,
                item.TypeName,
                item.PlacementClassification ?? string.Empty,
                item.Active));
            if (options.Count >= 80)
            {
                break;
            }
        }

        return options;
    }
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
        var selectable = rooms.Where(room => room.Band != "rejected" && room.RoomRef.Length > 0);
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
