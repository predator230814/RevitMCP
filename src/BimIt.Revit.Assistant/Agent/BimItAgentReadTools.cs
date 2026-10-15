using System.Text.Json;
using System.Text.Json.Nodes;
using BimIt.Revit.Assistant.Ipc;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Assistant;

internal sealed class BimItAgentReadTools
{
    public const string ModelContext = "bimit_get_model_context";

    public const string Warnings = "bimit_get_warnings";

    public const string ActiveViewElements = "bimit_get_active_view_elements";

    public const string InspectSelection = "bimit_inspect_selection";

    public const int ActiveViewLimit = 25;

    public const int InspectSelectionLimit = 10;

    public static readonly string[] Names = [ModelContext, Warnings, ActiveViewElements, InspectSelection];

    private static readonly string[] DeniedTools =
    [
        "revit_preview_parameter_updates",
        "revit_request_parameter_update_review",
        "revit_apply_parameter_updates",
        "runCSharp",
        "runPython",
        "executeRevitApi",
        "shell",
    ];

    private readonly object _usedLock = new();
    private readonly List<string> _used = [];
    private readonly IAssistantToolSession _session;
    private readonly string _instanceId;

    public BimItAgentReadTools(IAssistantToolSession session, string instanceId)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            throw new ArgumentException("An exact instance_id is required.", nameof(instanceId));
        }

        _instanceId = instanceId;
    }

    public static bool IsExposed(string? toolName)
    {
        return toolName is not null && Names.Contains(toolName, StringComparer.Ordinal);
    }

    public IReadOnlyList<string> ConsumeUsedTools()
    {
        lock (_usedLock)
        {
            var copy = _used.ToArray();
            _used.Clear();
            return copy;
        }
    }

    public async Task<IReadOnlyDictionary<string, object?>> InvokeAsync(string toolName, CancellationToken cancellationToken)
    {
        if (!IsExposed(toolName) || DeniedTools.Contains(toolName, StringComparer.Ordinal))
        {
            return Fail("tool_not_allowed", "That tool is not available to BIM-IT Assistant.");
        }

        return toolName switch
        {
            ModelContext => await ModelContextAsync(cancellationToken).ConfigureAwait(false),
            Warnings => await WarningsAsync(cancellationToken).ConfigureAwait(false),
            ActiveViewElements => await ActiveViewAsync(cancellationToken).ConfigureAwait(false),
            InspectSelection => await InspectSelectionAsync(cancellationToken).ConfigureAwait(false),
            _ => Fail("tool_not_allowed", "That tool is not available to BIM-IT Assistant."),
        };
    }

    private async Task<IReadOnlyDictionary<string, object?>> ModelContextAsync(CancellationToken cancellationToken)
    {
        RecordUse(ModelContext);
        var call = await _session.CallAsync(
            AssistantTools.GetContext,
            new Dictionary<string, object?> { ["instance_id"] = _instanceId },
            cancellationToken).ConfigureAwait(false);
        return Shape(call, "The context result could not be read.");
    }

    private async Task<IReadOnlyDictionary<string, object?>> WarningsAsync(CancellationToken cancellationToken)
    {
        RecordUse(Warnings);
        var context = await ReadContextAsync(cancellationToken).ConfigureAwait(false);
        if (context.Error is not null)
        {
            return context.Error;
        }

        if (!context.DocumentPresent)
        {
            return Fail(CapabilityErrorCodes.NoActiveDocument, "No active document is open, so warnings were not requested.");
        }

        if (context.DocumentId is null)
        {
            return Fail(
                "bridge_v12_required",
                "The connected BIM-IT Revit MCP host did not include document_id on the active document. Bridge protocol v12 is required. No document id was invented, and revit_get_warnings was not called.");
        }

        var warnings = await _session.CallAsync(
            AssistantTools.GetWarnings,
            new Dictionary<string, object?>
            {
                ["instance_id"] = _instanceId,
                ["document_id"] = context.DocumentId,
            },
            cancellationToken).ConfigureAwait(false);
        return Shape(warnings, "The warnings result could not be read.");
    }

    private async Task<IReadOnlyDictionary<string, object?>> ActiveViewAsync(CancellationToken cancellationToken)
    {
        RecordUse(ActiveViewElements);
        var context = await ReadContextAsync(cancellationToken).ConfigureAwait(false);
        if (context.Error is not null)
        {
            return context.Error;
        }

        if (!context.DocumentPresent)
        {
            return Fail(CapabilityErrorCodes.NoActiveDocument, "No active document is open, so elements were not queried.");
        }

        if (!context.ViewPresent)
        {
            return Fail(CapabilityErrorCodes.NoActiveView, "No active view is available, so elements were not queried.");
        }

        var arguments = new Dictionary<string, object?>
        {
            ["instance_id"] = _instanceId,
            ["scope"] = "active_view",
            ["filters"] = new Dictionary<string, object?>(),
            ["limit"] = ActiveViewLimit,
        };
        if (context.DocumentId is not null)
        {
            arguments["document_id"] = context.DocumentId;
        }

        var query = await _session.CallAsync(AssistantTools.QueryElements, arguments, cancellationToken).ConfigureAwait(false);
        return Shape(query, "The query result could not be read.");
    }

    private async Task<IReadOnlyDictionary<string, object?>> InspectSelectionAsync(CancellationToken cancellationToken)
    {
        RecordUse(InspectSelection);
        var selection = await _session.CallAsync(
            AssistantTools.GetSelection,
            new Dictionary<string, object?>
            {
                ["instance_id"] = _instanceId,
                ["limit"] = InspectSelectionLimit,
            },
            cancellationToken).ConfigureAwait(false);
        if (selection.IsError)
        {
            return Fail(
                string.IsNullOrWhiteSpace(selection.ErrorCode) ? "mcp_error" : selection.ErrorCode,
                string.IsNullOrWhiteSpace(selection.ErrorMessage) ? "The MCP tool returned an error." : selection.ErrorMessage);
        }

        if (!TryReadSelection(selection.Structured, out var documentId, out var selectedCount, out var truncated, out var refs))
        {
            return Fail("malformed", "The selection result could not be read.");
        }

        if (refs.Count == 0)
        {
            return new Dictionary<string, object?>
            {
                ["ok"] = true,
                ["selected_count"] = selectedCount,
                ["inspected_count"] = 0,
                ["truncated"] = truncated,
                ["elements"] = Array.Empty<IReadOnlyDictionary<string, object?>>(),
            };
        }

        var elements = await _session.CallAsync(
            AssistantTools.GetElements,
            new Dictionary<string, object?>
            {
                ["instance_id"] = _instanceId,
                ["document_id"] = documentId,
                ["element_refs"] = refs,
                ["projection"] = new Dictionary<string, object?>
                {
                    ["fields"] = new[]
                    {
                        "name",
                        "category_name",
                        "family_name",
                        "type_name",
                        "level_name",
                    },
                },
            },
            cancellationToken).ConfigureAwait(false);
        if (elements.IsError)
        {
            return Fail(
                string.IsNullOrWhiteSpace(elements.ErrorCode) ? "mcp_error" : elements.ErrorCode,
                string.IsNullOrWhiteSpace(elements.ErrorMessage) ? "The MCP tool returned an error." : elements.ErrorMessage);
        }

        if (!TryReadElements(elements.Structured, out var items))
        {
            return Fail("malformed", "The element result could not be read.");
        }

        return new Dictionary<string, object?>
        {
            ["ok"] = true,
            ["selected_count"] = selectedCount,
            ["inspected_count"] = items.Count,
            ["truncated"] = truncated,
            ["elements"] = items,
        };
    }

    private async Task<ContextRead> ReadContextAsync(CancellationToken cancellationToken)
    {
        var call = await _session.CallAsync(
            AssistantTools.GetContext,
            new Dictionary<string, object?> { ["instance_id"] = _instanceId },
            cancellationToken).ConfigureAwait(false);
        if (call.IsError)
        {
            return new ContextRead(Shape(call, "The context result could not be read."), false, false, null);
        }

        if (!AssistantWorkflows.TryReadContext(call.Structured, out var documentPresent, out var viewPresent, out var documentId))
        {
            return new ContextRead(Fail("malformed", "The context result could not be read."), false, false, null);
        }

        return new ContextRead(null, documentPresent, viewPresent, documentId);
    }

    private static IReadOnlyDictionary<string, object?> Shape(AssistantToolCall call, string malformedMessage)
    {
        if (call.IsError)
        {
            return Fail(
                string.IsNullOrWhiteSpace(call.ErrorCode) ? "mcp_error" : call.ErrorCode,
                string.IsNullOrWhiteSpace(call.ErrorMessage) ? "The MCP tool returned an error." : call.ErrorMessage);
        }

        var result = Clone(call.Structured);
        if (result is null)
        {
            return Fail("malformed", malformedMessage);
        }

        return new Dictionary<string, object?>
        {
            ["ok"] = true,
            ["result"] = result,
        };
    }

    private void RecordUse(string toolName)
    {
        lock (_usedLock)
        {
            if (_used.Count >= AssistantIpc.MaxUsedToolCount || !IsExposed(toolName))
            {
                return;
            }

            _used.Add(toolName);
        }
    }

    private static IReadOnlyDictionary<string, object?> Fail(string code, string message)
    {
        return new Dictionary<string, object?>
        {
            ["ok"] = false,
            ["code"] = code,
            ["message"] = AssistantSafeText.Clean(message, 1_000),
        };
    }

    private static JsonNode? Clone(JsonElement? structured)
    {
        if (structured is not { ValueKind: JsonValueKind.Object or JsonValueKind.Array } json)
        {
            return null;
        }

        return JsonNode.Parse(json.GetRawText());
    }

    private static bool TryReadSelection(
        JsonElement? structured,
        out string documentId,
        out int selectedCount,
        out bool truncated,
        out List<string> refs)
    {
        documentId = "";
        selectedCount = 0;
        truncated = false;
        refs = [];
        if (structured is not { ValueKind: JsonValueKind.Object } json
            || !json.TryGetProperty("context", out var context)
            || context.ValueKind != JsonValueKind.Object
            || !context.TryGetProperty("document_id", out var documentProperty)
            || documentProperty.ValueKind != JsonValueKind.String
            || documentProperty.GetString() is not string document
            || !json.TryGetProperty("selected_count", out var countProperty)
            || !countProperty.TryGetInt32(out selectedCount)
            || selectedCount < 0
            || !json.TryGetProperty("truncated", out var truncatedProperty)
            || truncatedProperty.ValueKind is not JsonValueKind.True and not JsonValueKind.False
            || !json.TryGetProperty("element_refs", out var refsProperty)
            || refsProperty.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        truncated = truncatedProperty.GetBoolean();
        foreach (var item in refsProperty.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || item.GetString() is not string elementRef)
            {
                return false;
            }

            refs.Add(elementRef);
        }

        documentId = document;
        return true;
    }

    private static bool TryReadElements(JsonElement? structured, out List<IReadOnlyDictionary<string, object?>> items)
    {
        items = [];
        if (structured is not { ValueKind: JsonValueKind.Object } json)
        {
            return false;
        }

        GetElementsResult? result;
        try
        {
            result = json.Deserialize<GetElementsResult>(ContractJson.Options);
        }
        catch (JsonException)
        {
            return false;
        }

        if (result?.Elements is null)
        {
            return false;
        }

        foreach (var element in result.Elements)
        {
            if (string.IsNullOrEmpty(element.ElementRef) || StatusName(element.Status) is not string status)
            {
                return false;
            }

            items.Add(new Dictionary<string, object?>
            {
                ["element_ref"] = element.ElementRef,
                ["status"] = status,
                ["name"] = Projected(element.Name),
                ["category_name"] = Projected(element.CategoryName),
                ["family_name"] = Projected(element.FamilyName),
                ["type_name"] = Projected(element.TypeName),
                ["level_name"] = Projected(element.LevelName),
            });
        }

        return true;
    }

    private static string? Projected(ProjectedString value)
    {
        return value.IsRequested ? value.Value : null;
    }

    private static string? StatusName(GetElementResultStatus status)
    {
        return status switch
        {
            GetElementResultStatus.Ok => "ok",
            GetElementResultStatus.NotFound => "not_found",
            _ => null,
        };
    }

    private sealed record ContextRead(
        IReadOnlyDictionary<string, object?>? Error,
        bool DocumentPresent,
        bool ViewPresent,
        string? DocumentId);
}
