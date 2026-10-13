using System.Text.Json;
using System.Text.Json.Nodes;
using BimIt.Revit.Assistant.Ipc;
using BimIt.Revit.Contracts;
using Xunit;

namespace BimIt.Revit.Assistant.Tests;

public sealed class AssistantWorkflowTests
{
    private const string InstanceId = "11111111-2222-3333-4444-555555555555";

    [Fact]
    public void Status_shaping_does_not_invent_an_ai_provider()
    {
        var starting = AssistantStatusShaping.Shape(true, "starting", null);
        Assert.Equal("ready", starting.RevitBridge);
        Assert.Equal("starting", starting.Assistant);
        Assert.Equal("starting", starting.Mcp);
        Assert.Equal("Not configured", starting.AiProvider);

        var ready = AssistantStatusShaping.Shape(
            true,
            "ready",
            new AssistantMcpReadiness(true, 10, ["revit_get_context", "revit_get_warnings"], null));
        Assert.Equal("ready", ready.Mcp);
        Assert.Equal(10, ready.ToolCount);
        Assert.Equal("Not configured", ready.AiProvider);
        Assert.Equal(AssistantStatusShaping.AiProviderNotConfigured, ready.AiProvider);

        var failed = AssistantStatusShaping.Shape(false, "unavailable", new AssistantMcpReadiness(false, 0, [], "pipe closed"));
        Assert.Equal("unavailable", failed.RevitBridge);
        Assert.Equal("unavailable", failed.Mcp);
        Assert.Equal("pipe closed", failed.Error);
        Assert.Equal("Not configured", failed.AiProvider);
    }

    [Fact]
    public async Task Context_workflow_uses_the_exact_instance_id()
    {
        var session = new RecordingSession(_ => Success(ContextJson()));
        var result = await AssistantWorkflows.RunAsync(session, InstanceId, AssistantWorkflowName.Context, CancellationToken.None);

        var call = Assert.Single(session.Calls);
        Assert.Equal(AssistantTools.GetContext, call.Tool);
        Assert.Equal(InstanceId, call.Arguments["instance_id"]);
        Assert.False(result.IsError);
        Assert.Contains("Sample", result.Body, StringComparison.Ordinal);
        Assert.Contains("Level 1", result.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Warnings_stop_when_there_is_no_active_document()
    {
        var session = new RecordingSession(_ => Success("""{"document":null,"active_view":null}"""));
        var result = await AssistantWorkflows.RunAsync(session, InstanceId, AssistantWorkflowName.Warnings, CancellationToken.None);

        Assert.Equal(AssistantTools.GetContext, Assert.Single(session.Calls).Tool);
        Assert.Equal(InstanceId, session.Calls[0].Arguments["instance_id"]);
        Assert.Equal(CapabilityErrorCodes.NoActiveDocument, result.ErrorCode);
        Assert.Contains("No active document", result.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Warnings_do_not_invent_a_document_id()
    {
        var session = new RecordingSession(_ => Success(ContextJson()));
        var result = await AssistantWorkflows.RunAsync(session, InstanceId, AssistantWorkflowName.Warnings, CancellationToken.None);

        Assert.DoesNotContain(session.Calls, call => call.Tool == AssistantTools.GetWarnings);
        Assert.Equal("bridge_v12_required", result.ErrorCode);
        Assert.Contains("Bridge protocol v12", result.Body, StringComparison.Ordinal);
        Assert.True(result.IsError);
    }

    [Fact]
    public async Task Warnings_use_the_exact_instance_and_supplied_document_id()
    {
        var session = new RecordingSession(tool => tool == AssistantTools.GetContext
            ? Success(ContextJson(documentId: "doc-9"))
            : Success(WarningsJson(matched: 2, truncated: true)));
        var result = await AssistantWorkflows.RunAsync(session, InstanceId, AssistantWorkflowName.Warnings, CancellationToken.None);

        Assert.Equal(2, session.Calls.Count);
        Assert.All(session.Calls, call => Assert.Equal(InstanceId, call.Arguments["instance_id"]));
        Assert.Equal("doc-9", session.Calls[1].Arguments["document_id"]);
        Assert.Equal(2, session.Calls[1].Arguments.Count);
        Assert.False(result.IsError);
        Assert.Contains("Matched 2", result.Body, StringComparison.Ordinal);
        Assert.Contains("Door clash", result.Body, StringComparison.Ordinal);
        Assert.Contains("truncated", result.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Warnings_render_zero_results()
    {
        var session = new RecordingSession(tool => tool == AssistantTools.GetContext
            ? Success(ContextJson(documentId: "doc-9"))
            : Success(WarningsJson(matched: 0, truncated: false)));
        var result = await AssistantWorkflows.RunAsync(session, InstanceId, AssistantWorkflowName.Warnings, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Contains("Matched 0", result.Body, StringComparison.Ordinal);
        Assert.Contains("No warnings were returned", result.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Active_view_stops_without_a_document_or_view()
    {
        var noDocument = new RecordingSession(_ => Success("""{"document":null,"active_view":null}"""));
        var documentResult = await AssistantWorkflows.RunAsync(noDocument, InstanceId, AssistantWorkflowName.ActiveView, CancellationToken.None);
        Assert.Equal(CapabilityErrorCodes.NoActiveDocument, documentResult.ErrorCode);
        Assert.Single(noDocument.Calls);

        var noView = new RecordingSession(_ => Success(ContextJson(includeView: false)));
        var viewResult = await AssistantWorkflows.RunAsync(noView, InstanceId, AssistantWorkflowName.ActiveView, CancellationToken.None);
        Assert.Equal(CapabilityErrorCodes.NoActiveView, viewResult.ErrorCode);
        Assert.Equal(AssistantTools.GetContext, Assert.Single(noView.Calls).Tool);
    }

    [Fact]
    public async Task Active_view_sends_the_accepted_query_shape_and_exact_instance()
    {
        var session = new RecordingSession(tool => tool == AssistantTools.GetContext
            ? Success(ContextJson())
            : Success(QueryJson()));
        var result = await AssistantWorkflows.RunAsync(session, InstanceId, AssistantWorkflowName.ActiveView, CancellationToken.None);

        var query = session.Calls[1];
        Assert.Equal(AssistantTools.QueryElements, query.Tool);
        Assert.Equal(InstanceId, query.Arguments["instance_id"]);
        Assert.Equal("active_view", query.Arguments["scope"]);
        Assert.Equal(25, query.Arguments["limit"]);
        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(query.Arguments["filters"]));
        Assert.False(query.Arguments.ContainsKey("document_id"));
        Assert.Contains("Matched 3", result.Body, StringComparison.Ordinal);
        Assert.Contains("ref-1", result.Body, StringComparison.Ordinal);
        Assert.Contains("Truncated", result.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Active_view_explains_the_empty_filter_contract_error()
    {
        var session = new RecordingSession(tool => tool == AssistantTools.GetContext
            ? Success(ContextJson(documentId: "doc-9"))
            : Error("INVALID_REQUEST", "filters"));
        var result = await AssistantWorkflows.RunAsync(session, InstanceId, AssistantWorkflowName.ActiveView, CancellationToken.None);

        Assert.Equal("doc-9", session.Calls[1].Arguments["document_id"]);
        Assert.Equal(InstanceId, session.Calls[1].Arguments["instance_id"]);
        Assert.True(result.IsError);
        Assert.Contains("at least one filter", result.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Mcp_failure_is_rendered_and_does_not_call_another_tool()
    {
        var session = new RecordingSession(_ => Error("NO_REVIT_INSTANCE", "No eligible Revit instance is available."));
        var result = await AssistantWorkflows.RunAsync(session, InstanceId, AssistantWorkflowName.Context, CancellationToken.None);

        Assert.Equal(InstanceId, Assert.Single(session.Calls).Arguments["instance_id"]);
        Assert.True(result.IsError);
        Assert.Equal("NO_REVIT_INSTANCE", result.ErrorCode);
        Assert.Contains("No eligible Revit instance", result.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void Trusted_write_tools_are_not_assistant_workflows()
    {
        Assert.True(AssistantTools.IsTrustedWriteTool("revit_apply_parameter_updates"));
        Assert.True(AssistantTools.IsTrustedWriteTool("revit_preview_parameter_updates"));
        Assert.True(AssistantTools.IsTrustedWriteTool("revit_request_parameter_update_review"));
        Assert.DoesNotContain(AssistantTools.WorkflowTools, AssistantTools.IsTrustedWriteTool);
        Assert.False(AssistantIpc.Parse("""{"type":"approveIntent"}""").Accepted);
        Assert.False(AssistantIpc.Parse("""{"type":"hello","instanceId":"a","extra":1}""").Accepted);
        Assert.False(AssistantIpc.Parse("""{"type":"runWorkflow","requestId":"r","workflow":"apply"}""").Accepted);
        Assert.False(AssistantIpc.Parse("").Accepted);
        Assert.Equal(AssistantWorkflowName.Context, AssistantIpc.Parse("""{"type":"runWorkflow","requestId":"r","workflow":"context"}""").Message!.Workflow);
    }

    private static string ContextJson(string? documentId = null, bool includeView = true)
    {
        var result = new GetContextResult
        {
            Instance = new GetContextInstance
            {
                InstanceId = InstanceId,
                RevitVersion = "2026",
                RevitBuild = "26.5.0.55",
            },
            Document = new GetContextDocument
            {
                Title = "Sample",
                Kind = GetContextDocumentKind.Project,
                IsWorkshared = false,
                IsModelInCloud = false,
                IsReadOnly = false,
                IsModified = false,
            },
            ActiveView = includeView
                ? new GetContextActiveView { ElementId = "view-1", Name = "Level 1", ViewType = "FloorPlan" }
                : null,
            Selection = new GetContextSelection { Count = 0 },
        };
        var node = JsonNode.Parse(JsonSerializer.Serialize(result, ContractJson.Options))!;
        if (documentId is not null)
        {
            node["document"]!["document_id"] = documentId;
        }

        return node.ToJsonString();
    }

    private static string WarningsJson(int matched, bool truncated)
    {
        var result = new GetWarningsResult
        {
            Context = new GetWarningsContext { InstanceId = InstanceId, DocumentId = "doc-9" },
            MatchedCount = matched,
            CountsBySeverity = new GetWarningsCounts
            {
                Warning = matched,
                Error = 0,
                DocumentCorruption = 0,
                Other = 0,
            },
            Definitions = matched == 0
                ? []
                : [new GetWarningsDefinition { FailureKey = "door", Severity = WarningSeverity.Warning, MatchedCount = matched }],
            Warnings = matched == 0
                ? []
                : [new GetWarningsItem
                {
                    FailureKey = "door",
                    Severity = WarningSeverity.Warning,
                    DescriptionText = "Door clash",
                    DescriptionTruncated = false,
                    HasResolutions = false,
                    Elements = [],
                    ElementsTruncated = false,
                    UnresolvedElementCount = 0,
                }],
            Truncated = truncated,
            TruncationReasons = truncated ? [WarningTruncationReason.Warnings] : [],
        };
        return JsonSerializer.Serialize(result, ContractJson.Options);
    }

    private static string QueryJson()
    {
        var result = new QueryElementsResult
        {
            Context = new QueryElementsContext { InstanceId = InstanceId, DocumentId = "doc-9" },
            MatchedCount = 3,
            Truncated = true,
            ElementRefs = ["ref-1", "ref-2"],
        };
        return JsonSerializer.Serialize(result, ContractJson.Options);
    }

    private static AssistantToolCall Success(string json)
        => new(false, null, null, JsonDocument.Parse(json).RootElement.Clone());

    private static AssistantToolCall Error(string code, string message)
        => new(true, code, message, null);

    private sealed class RecordingSession(Func<string, AssistantToolCall> responder) : IAssistantToolSession
    {
        public List<(string Tool, IReadOnlyDictionary<string, object?> Arguments)> Calls { get; } = [];

        public Task<AssistantMcpReadiness> InitializeAsync(CancellationToken cancellationToken)
            => Task.FromResult(new AssistantMcpReadiness(true, 10, AssistantTools.WorkflowTools, null));

        public Task<AssistantToolCall> CallAsync(
            string toolName,
            IReadOnlyDictionary<string, object?> arguments,
            CancellationToken cancellationToken)
        {
            Calls.Add((toolName, arguments));
            return Task.FromResult(responder(toolName));
        }
    }
}
