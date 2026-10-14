using System.Text.Json;
using System.Text.Json.Nodes;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Assistant;

internal sealed class BimItAgentReadTools
{
    public const string ModelContext = "bimit_get_model_context";

    public const string Warnings = "bimit_get_warnings";

    public const string ActiveViewElements = "bimit_get_active_view_elements";

    public const int ActiveViewLimit = 25;

    public static readonly string[] Names = [ModelContext, Warnings, ActiveViewElements];

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
            _ => Fail("tool_not_allowed", "That tool is not available to BIM-IT Assistant."),
        };
    }

    private async Task<IReadOnlyDictionary<string, object?>> ModelContextAsync(CancellationToken cancellationToken)
    {
        var call = await _session.CallAsync(
            AssistantTools.GetContext,
            new Dictionary<string, object?> { ["instance_id"] = _instanceId },
            cancellationToken).ConfigureAwait(false);
        return Shape(call, "The context result could not be read.");
    }

    private async Task<IReadOnlyDictionary<string, object?>> WarningsAsync(CancellationToken cancellationToken)
    {
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

    private sealed record ContextRead(
        IReadOnlyDictionary<string, object?>? Error,
        bool DocumentPresent,
        bool ViewPresent,
        string? DocumentId);
}
