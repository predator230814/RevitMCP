using System.Text.Json;
using BimIt.Revit.Assistant.Ipc;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Assistant;

public interface IAssistantToolSession
{
    Task<AssistantMcpReadiness> InitializeAsync(CancellationToken cancellationToken);

    Task<AssistantToolCall> CallAsync(
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken);
}

public sealed record AssistantMcpReadiness(bool Ready, int ToolCount, IReadOnlyList<string> ToolNames, string? Error);

public sealed record AssistantToolCall(bool IsError, string? ErrorCode, string? ErrorMessage, JsonElement? Structured);

public sealed record AssistantWorkflowResult(string Title, string Body, bool IsError, string? ErrorCode);

public static class AssistantTools
{
    public const string GetContext = "revit_get_context";

    public const string GetWarnings = "revit_get_warnings";

    public const string QueryElements = "revit_query_elements";

    public static readonly string[] WorkflowTools = [GetContext, GetWarnings, QueryElements];

    public static bool IsTrustedWriteTool(string toolName)
    {
        return toolName is "revit_apply_parameter_updates"
            or "revit_preview_parameter_updates"
            or "revit_request_parameter_update_review";
    }
}

public static class AssistantStatusShaping
{
    public const string AiProviderNotConfigured = "Not configured";

    public static AssistantConnectionStatus Shape(bool bridgeReady, string assistantState, AssistantMcpReadiness? mcp)
    {
        var ready = mcp?.Ready == true;
        return new AssistantConnectionStatus(
            bridgeReady ? "ready" : "unavailable",
            assistantState,
            mcp is null ? "starting" : ready ? "ready" : "unavailable",
            AiProviderNotConfigured,
            mcp?.ToolCount,
            mcp?.ToolNames ?? [],
            mcp?.Error);
    }
}

public sealed record AssistantConnectionStatus(
    string RevitBridge,
    string Assistant,
    string Mcp,
    string AiProvider,
    int? ToolCount,
    IReadOnlyList<string> ToolNames,
    string? Error);

public static class AssistantWorkflows
{
    public static async Task<AssistantWorkflowResult> RunAsync(
        IAssistantToolSession session,
        string instanceId,
        AssistantWorkflowName workflow,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            throw new ArgumentException("An exact instance_id is required.", nameof(instanceId));
        }

        return workflow switch
        {
            AssistantWorkflowName.Context => await ContextAsync(session, instanceId, cancellationToken).ConfigureAwait(false),
            AssistantWorkflowName.Warnings => await WarningsAsync(session, instanceId, cancellationToken).ConfigureAwait(false),
            AssistantWorkflowName.ActiveView => await ActiveViewAsync(session, instanceId, cancellationToken).ConfigureAwait(false),
            _ => new AssistantWorkflowResult("Unknown workflow", "That workflow is not available.", true, "unknown"),
        };
    }

    private static async Task<AssistantWorkflowResult> ContextAsync(
        IAssistantToolSession session,
        string instanceId,
        CancellationToken cancellationToken)
    {
        var call = await session.CallAsync(
            AssistantTools.GetContext,
            new Dictionary<string, object?> { ["instance_id"] = instanceId },
            cancellationToken).ConfigureAwait(false);
        if (call.IsError)
        {
            return Error("Model context", call);
        }

        var context = Deserialize<GetContextResult>(call.Structured);
        if (context is null)
        {
            return new AssistantWorkflowResult("Model context", "The context result could not be read.", true, "malformed");
        }

        return new AssistantWorkflowResult("Model context", RenderContext(context), false, null);
    }

    private static async Task<AssistantWorkflowResult> WarningsAsync(
        IAssistantToolSession session,
        string instanceId,
        CancellationToken cancellationToken)
    {
        var contextCall = await session.CallAsync(
            AssistantTools.GetContext,
            new Dictionary<string, object?> { ["instance_id"] = instanceId },
            cancellationToken).ConfigureAwait(false);
        if (contextCall.IsError)
        {
            return Error("Warnings", contextCall);
        }

        if (!TryReadContext(contextCall.Structured, out var documentPresent, out var viewPresent, out var documentId))
        {
            return new AssistantWorkflowResult("Warnings", "The context result could not be read.", true, "malformed");
        }

        if (!documentPresent)
        {
            return new AssistantWorkflowResult(
                "Warnings",
                "No active document is open, so warnings were not requested.",
                false,
                CapabilityErrorCodes.NoActiveDocument);
        }

        if (documentId is null)
        {
            return new AssistantWorkflowResult(
                "Warnings",
                "revit_get_context confirmed an active document, but that accepted result does not include document_id. revit_get_warnings requires document_id, so this slice does not invent one or call the warnings tool.",
                true,
                "document_id_unavailable");
        }

        var warnings = await session.CallAsync(
            AssistantTools.GetWarnings,
            new Dictionary<string, object?>
            {
                ["instance_id"] = instanceId,
                ["document_id"] = documentId,
            },
            cancellationToken).ConfigureAwait(false);
        if (warnings.IsError)
        {
            return Error("Warnings", warnings);
        }

        var result = Deserialize<GetWarningsResult>(warnings.Structured);
        if (result is null)
        {
            return new AssistantWorkflowResult("Warnings", "The warnings result could not be read.", true, "malformed");
        }

        return new AssistantWorkflowResult("Warnings", RenderWarnings(result), false, null);
    }

    private static async Task<AssistantWorkflowResult> ActiveViewAsync(
        IAssistantToolSession session,
        string instanceId,
        CancellationToken cancellationToken)
    {
        var contextCall = await session.CallAsync(
            AssistantTools.GetContext,
            new Dictionary<string, object?> { ["instance_id"] = instanceId },
            cancellationToken).ConfigureAwait(false);
        if (contextCall.IsError)
        {
            return Error("Active view", contextCall);
        }

        if (!TryReadContext(contextCall.Structured, out var documentPresent, out var viewPresent, out var documentId))
        {
            return new AssistantWorkflowResult("Active view", "The context result could not be read.", true, "malformed");
        }

        if (!documentPresent)
        {
            return new AssistantWorkflowResult(
                "Active view",
                "No active document is open, so elements were not queried.",
                false,
                CapabilityErrorCodes.NoActiveDocument);
        }

        if (!viewPresent)
        {
            return new AssistantWorkflowResult(
                "Active view",
                "No active view is available, so elements were not queried.",
                false,
                CapabilityErrorCodes.NoActiveView);
        }

        var arguments = new Dictionary<string, object?>
        {
            ["instance_id"] = instanceId,
            ["scope"] = "active_view",
            ["filters"] = new Dictionary<string, object?>(),
            ["limit"] = 25,
        };
        if (documentId is not null)
        {
            arguments["document_id"] = documentId;
        }

        var query = await session.CallAsync(AssistantTools.QueryElements, arguments, cancellationToken).ConfigureAwait(false);
        if (query.IsError)
        {
            var error = Error("Active view", query);
            if (query.ErrorCode is "INVALID_REQUEST" or CapabilityErrorCodes.InvalidQuery)
            {
                return error with
                {
                    Body = error.Body + " The accepted revit_query_elements contract requires at least one filter. This action sent an empty filter object and does not add a new query capability.",
                };
            }

            return error;
        }

        var result = Deserialize<QueryElementsResult>(query.Structured);
        if (result is null)
        {
            return new AssistantWorkflowResult("Active view", "The query result could not be read.", true, "malformed");
        }

        return new AssistantWorkflowResult("Active view", RenderQuery(result), false, null);
    }

    internal static bool TryReadContext(JsonElement? structured, out bool documentPresent, out bool viewPresent, out string? documentId)
    {
        documentPresent = false;
        viewPresent = false;
        documentId = null;
        if (structured is not { ValueKind: JsonValueKind.Object } json)
        {
            return false;
        }

        if (!json.TryGetProperty("document", out var document))
        {
            return false;
        }

        documentPresent = document.ValueKind == JsonValueKind.Object;
        if (documentPresent && document.TryGetProperty("document_id", out var id) && id.ValueKind == JsonValueKind.String)
        {
            var text = id.GetString();
            documentId = string.IsNullOrEmpty(text) ? null : text;
        }

        if (!json.TryGetProperty("active_view", out var view))
        {
            return false;
        }

        viewPresent = view.ValueKind == JsonValueKind.Object;
        return true;
    }

    private static string RenderContext(GetContextResult context)
    {
        var document = context.Document is null
            ? "No active document."
            : $"Document: {context.Document.Title} ({context.Document.Kind}). Modified: {context.Document.IsModified}. Read-only: {context.Document.IsReadOnly}.";
        var view = context.ActiveView is null
            ? "No active view."
            : $"View: {context.ActiveView.Name} ({context.ActiveView.ViewType}).";
        return $"Revit {context.Instance.RevitVersion} ({context.Instance.RevitBuild}). {document} {view} Selection count: {context.Selection.Count}.";
    }

    private static string RenderWarnings(GetWarningsResult result)
    {
        var counts = result.CountsBySeverity;
        var definitions = result.Definitions.Count == 0
            ? "No definitions."
            : string.Join("; ", result.Definitions.Take(8).Select(row => $"{row.FailureKey} {row.Severity} x{row.MatchedCount}"));
        var messages = result.Warnings.Count == 0
            ? "No warnings were returned."
            : string.Join(" ", result.Warnings.Take(8).Select(item => $"{item.Severity}: {item.DescriptionText}"));
        var truncated = result.Truncated
            ? " Response truncated (" + string.Join(", ", result.TruncationReasons) + ")."
            : "";
        return $"Matched {result.MatchedCount}. Warning {counts.Warning}, error {counts.Error}, document corruption {counts.DocumentCorruption}, other {counts.Other}. {definitions} {messages}{truncated}";
    }

    private static string RenderQuery(QueryElementsResult result)
    {
        var refs = result.ElementRefs.Count == 0
            ? "none"
            : string.Join(", ", result.ElementRefs);
        var truncated = result.Truncated ? " Truncated." : "";
        return $"Matched {result.MatchedCount}. Returned {result.ElementRefs.Count}: {refs}.{truncated}";
    }

    private static AssistantWorkflowResult Error(string title, AssistantToolCall call)
    {
        var code = call.ErrorCode ?? "mcp_error";
        var message = string.IsNullOrWhiteSpace(call.ErrorMessage) ? "The MCP tool returned an error." : call.ErrorMessage;
        return new AssistantWorkflowResult(title, code + ": " + message, true, code);
    }

    private static T? Deserialize<T>(JsonElement? structured)
    {
        if (structured is not { ValueKind: JsonValueKind.Object } json)
        {
            return default;
        }

        return json.Deserialize<T>(ContractJson.Options);
    }
}
