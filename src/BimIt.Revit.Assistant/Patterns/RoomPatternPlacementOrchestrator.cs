using System.Globalization;
using System.Text.Json;
using BimIt.Revit.Assistant.Patterns;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Assistant;

internal interface IRoomPatternPreviewClient
{
    public const string ToolName = "revit_preview_room_pattern_placements";

    Task<AssistantToolCall> PreviewAsync(
        string instanceId,
        PreviewRoomPatternPlacementsRequest request,
        CancellationToken cancellationToken);
}

internal static class PatternConfigurationParser
{
    public static bool TryParse(
        string? json,
        RoomPatternGeneration generation,
        out PatternLocalConfiguration configuration,
        out string failure)
    {
        configuration = null!;
        failure = "Choose target rooms, a loaded type, and a mounting height for every role.";
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return false;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Any(property => property.Name is not ("rooms" or "roles")))
            {
                return false;
            }

            if (!root.TryGetProperty("rooms", out var roomsProperty)
                || roomsProperty.ValueKind != JsonValueKind.Array
                || !root.TryGetProperty("roles", out var rolesProperty)
                || rolesProperty.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            var rooms = new List<int>();
            foreach (var room in roomsProperty.EnumerateArray())
            {
                if (room.ValueKind != JsonValueKind.Number || !room.TryGetInt32(out var index))
                {
                    return false;
                }

                rooms.Add(index);
            }

            var options = new Dictionary<string, string>(StringComparer.Ordinal);
            var heights = new Dictionary<string, int>();
            foreach (var role in rolesProperty.EnumerateArray())
            {
                if (role.ValueKind != JsonValueKind.Object
                    || role.EnumerateObject().Any(property => property.Name is not ("role" or "option" or "height_mm"))
                    || !role.TryGetProperty("role", out var roleProperty)
                    || roleProperty.ValueKind != JsonValueKind.String
                    || roleProperty.GetString() is not string roleName
                    || !role.TryGetProperty("option", out var optionProperty)
                    || optionProperty.ValueKind != JsonValueKind.String
                    || optionProperty.GetString() is not string optionKey
                    || !role.TryGetProperty("height_mm", out var heightProperty)
                    || heightProperty.ValueKind != JsonValueKind.Number
                    || !heightProperty.TryGetInt32(out var height))
                {
                    return false;
                }

                if (!options.TryAdd(roleName, optionKey))
                {
                    return false;
                }

                heights[roleName] = height;
            }

            configuration = new PatternLocalConfiguration
            {
                SelectedRooms = rooms,
                RoleOptions = options,
                HeightMillimetres = heights,
            };
            if (!configuration.Matches(generation))
            {
                failure = configuration.HeightMillimetres.Values.Any(height => height is < 0 or > 6000)
                    ? "Mounting height must be from 0 to 6000 mm."
                    : "Choose one to eight matching rooms and a loaded type for every role.";
                return false;
            }

            failure = "";
            return true;
        }
    }
}

internal static class PatternPlacementRequestBuilder
{
    public static PreviewRoomPatternPlacementsRequest Build(RoomPatternGeneration generation, PatternLocalConfiguration configuration)
    {
        var placements = new List<PreviewRoomPatternPlacement>();
        foreach (var index in configuration.SelectedRooms.OrderBy(value => value))
        {
            var room = generation.Rooms.First(item => item.Index == index);
            foreach (var device in room.Devices)
            {
                var option = generation.Options[configuration.RoleOptions[device.Role]];
                placements.Add(new PreviewRoomPatternPlacement
                {
                    RoomRef = room.RoomRef,
                    FamilyTypeRef = option.FamilyTypeRef,
                    Role = device.Role,
                    PlacementKind = device.PlacementKind,
                    XMetres = device.XMetres,
                    YMetres = device.YMetres,
                    MountingHeightMetres = configuration.HeightMillimetres[device.Role] / 1000d,
                    QuarterTurns = device.QuarterTurns,
                });
            }
        }

        return new PreviewRoomPatternPlacementsRequest
        {
            DocumentId = generation.DocumentId,
            Placements = placements,
        };
    }
}

internal sealed class RoomPatternPlacementOrchestrator
{
    public const string PreviewTitle = "Pattern preview";

    public const string ReviewTitle = "Pattern review";

    public const string ApplyTitle = "Pattern apply";

    private readonly IRoomPatternPreviewClient _preview;
    private readonly IMepDevicePlacementReviewClient _review;
    private readonly IMepDevicePlacementApplyClient _apply;
    private readonly RoomPatternStore _store;
    private readonly string _instanceId;

    public RoomPatternPlacementOrchestrator(
        IRoomPatternPreviewClient preview,
        IMepDevicePlacementReviewClient review,
        IMepDevicePlacementApplyClient apply,
        RoomPatternStore store,
        string instanceId)
    {
        _preview = preview ?? throw new ArgumentNullException(nameof(preview));
        _review = review ?? throw new ArgumentNullException(nameof(review));
        _apply = apply ?? throw new ArgumentNullException(nameof(apply));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _instanceId = instanceId ?? throw new ArgumentNullException(nameof(instanceId));
    }

    public async Task<MepDevicePlacementPreviewOutcome> PrepareAsync(string? configurationJson, CancellationToken cancellationToken)
    {
        if (!_store.TryCurrentPreparation(out var generation, out _, out var failure))
        {
            return Fail(PreviewTitle, failure);
        }

        if (!string.Equals(generation.InstanceId, _instanceId, StringComparison.Ordinal))
        {
            return Fail(PreviewTitle, "This pattern belongs to a different Revit session.");
        }

        if (!PatternConfigurationParser.TryParse(configurationJson, generation, out var configuration, out failure))
        {
            return Fail(PreviewTitle, failure);
        }

        if (!_store.TryConfigure(configuration))
        {
            return Fail(PreviewTitle, "The pattern configuration is no longer valid.");
        }

        var request = PatternPlacementRequestBuilder.Build(generation, configuration);
        AssistantToolCall call;
        try
        {
            call = await _preview.PreviewAsync(_instanceId, request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return Fail(PreviewTitle, "The pattern preview could not be created.");
        }

        if (call.IsError || call.Structured is not JsonElement structured)
        {
            return Fail(PreviewTitle, "The pattern preview could not be created.");
        }

        PreviewRoomPatternPlacementsResult? result;
        try
        {
            result = structured.Deserialize<PreviewRoomPatternPlacementsResult>(ContractJson.Options);
        }
        catch (JsonException)
        {
            result = null;
        }

        if (result is null || !result.Ready || string.IsNullOrWhiteSpace(result.IntentRef) || result.Items is null)
        {
            return Fail(PreviewTitle, Describe(result));
        }

        _store.RememberPreview(generation.GenerationId, result.IntentRef, result.Items.Count);
        return Ok(
            PreviewTitle,
            "Pattern preview is ready for "
            + result.RoomCount.ToString(CultureInfo.InvariantCulture)
            + " rooms and "
            + result.Items.Count.ToString(CultureInfo.InvariantCulture)
            + " devices. Review it in Revit before applying.");
    }

    public async Task<MepDevicePlacementPreviewOutcome> ReviewAsync(CancellationToken cancellationToken)
    {
        if (_store.TryTerminal(out var summary, out _))
        {
            return Fail(ReviewTitle, summary);
        }

        if (!_store.TryPreview(out var intentRef, out _))
        {
            return Fail(ReviewTitle, "Prepare a pattern preview before opening Revit review.");
        }

        return await ReviewOrApplyAsync(intentRef, review: true, cancellationToken).ConfigureAwait(false);
    }

    public async Task<MepDevicePlacementPreviewOutcome> ApplyAsync(CancellationToken cancellationToken)
    {
        if (_store.TryTerminal(out var summary, out var isError))
        {
            return new MepDevicePlacementPreviewOutcome(isError, ApplyTitle, summary);
        }

        if (!_store.TryPreview(out var intentRef, out var expected))
        {
            return Fail(ApplyTitle, "Prepare a pattern preview before applying.");
        }

        AssistantToolCall call;
        try
        {
            call = await _apply.ApplyAsync(_instanceId, intentRef, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return Fail(ApplyTitle, "The pattern apply could not be completed.");
        }

        if (call.IsError || call.Structured is not JsonElement structured)
        {
            return Fail(ApplyTitle, "The pattern apply could not be completed.");
        }

        ApplyMepDevicePlacementsResult? result;
        try
        {
            result = structured.Deserialize<ApplyMepDevicePlacementsResult>(ContractJson.Options);
        }
        catch (JsonException)
        {
            result = null;
        }

        if (result is null)
        {
            return Fail(ApplyTitle, "The pattern apply could not be completed.");
        }

        var generationId = _store.CurrentGeneration?.GenerationId ?? string.Empty;
        return result.Status switch
        {
            ApplyMepDevicePlacementsStatus.Applied when result.CreatedCount == expected => Terminal(generationId, "Applied " + expected.ToString(CultureInfo.InvariantCulture) + " pattern placements. Revit verification passed.", false),
            ApplyMepDevicePlacementsStatus.Applied => Fail(ApplyTitle, "The pattern apply could not be completed."),
            ApplyMepDevicePlacementsStatus.ApprovalRequired => Ok(ApplyTitle, "Revit-local approval is still required. No placements were created."),
            ApplyMepDevicePlacementsStatus.InProgress => Ok(ApplyTitle, "The pattern apply is already in progress."),
            ApplyMepDevicePlacementsStatus.Stale => Terminal(generationId, "This pattern preview is stale. No automatic retry will be attempted.", true),
            ApplyMepDevicePlacementsStatus.TransactionFailed => Terminal(generationId, "The pattern transaction failed. No automatic retry will be attempted.", true),
            ApplyMepDevicePlacementsStatus.CommittedUnverified => Terminal(generationId, "A transaction committed, but Revit verification failed. Do not retry this pattern.", true),
            ApplyMepDevicePlacementsStatus.Indeterminate => Terminal(generationId, "The pattern result is indeterminate. No automatic retry will be attempted.", true),
            ApplyMepDevicePlacementsStatus.AuditFailed => Terminal(generationId, "The pattern audit failed. No automatic retry will be attempted.", true),
            _ => Fail(ApplyTitle, "The pattern apply could not be completed."),
        };
    }

    private async Task<MepDevicePlacementPreviewOutcome> ReviewOrApplyAsync(string intentRef, bool review, CancellationToken cancellationToken)
    {
        AssistantToolCall call;
        try
        {
            call = await _review.ReviewAsync(_instanceId, intentRef, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return Fail(ReviewTitle, "The Revit review could not be opened.");
        }

        if (call.IsError || call.Structured is not JsonElement structured)
        {
            return Fail(ReviewTitle, "The Revit review could not be opened.");
        }

        RequestMepDevicePlacementReviewResult? result;
        try
        {
            result = structured.Deserialize<RequestMepDevicePlacementReviewResult>(ContractJson.Options);
        }
        catch (JsonException)
        {
            result = null;
        }

        if (result is null)
        {
            return Fail(ReviewTitle, "The Revit review could not be opened.");
        }

        return result.Status switch
        {
            RequestMepDevicePlacementReviewStatus.Started => Ok(ReviewTitle, "Revit review opened."),
            RequestMepDevicePlacementReviewStatus.AlreadyActive => Ok(ReviewTitle, "The same review is already open."),
            RequestMepDevicePlacementReviewStatus.Busy => Ok(ReviewTitle, "Another review is active."),
            RequestMepDevicePlacementReviewStatus.Terminal => Ok(ReviewTitle, "This intent already has a local terminal decision."),
            _ => Fail(ReviewTitle, "The Revit review could not be opened."),
        };
    }

    private MepDevicePlacementPreviewOutcome Terminal(string generationId, string summary, bool isError)
    {
        _store.RememberTerminal(generationId, summary, isError);
        return new MepDevicePlacementPreviewOutcome(isError, ApplyTitle, summary);
    }

    private static string Describe(PreviewRoomPatternPlacementsResult? result)
    {
        var status = result?.Items?.FirstOrDefault(item => item.Status != PreviewRoomPatternItemStatus.Ok)?.Status;
        return status switch
        {
            PreviewRoomPatternItemStatus.AmbiguousWall => "A pattern point did not resolve to one straight room wall.",
            PreviewRoomPatternItemStatus.CurvedWall => "Curved walls are not supported for this pattern.",
            PreviewRoomPatternItemStatus.NoWall => "A boundary device did not find a room wall.",
            PreviewRoomPatternItemStatus.UnsupportedFace => "The loaded type needs a wall face that could not be resolved.",
            PreviewRoomPatternItemStatus.UnsupportedPlacement => "The loaded type cannot be placed with this pattern.",
            PreviewRoomPatternItemStatus.OutsideRoom => "A projected point is outside the target room.",
            PreviewRoomPatternItemStatus.Occupied => "A target point is already occupied.",
            PreviewRoomPatternItemStatus.CategoryMismatch => "The selected type does not match the pattern role.",
            PreviewRoomPatternItemStatus.RoomNotFound => "A target room is no longer available.",
            PreviewRoomPatternItemStatus.TypeNotFound => "A selected type is no longer loaded.",
            PreviewRoomPatternItemStatus.DocumentMismatch => "The active document changed.",
            _ => "The pattern preview is not ready. No Revit elements were changed.",
        };
    }

    private static MepDevicePlacementPreviewOutcome Ok(string title, string body)
    {
        return new MepDevicePlacementPreviewOutcome(false, title, body);
    }

    private static MepDevicePlacementPreviewOutcome Fail(string title, string body)
    {
        return new MepDevicePlacementPreviewOutcome(true, title, body);
    }
}
