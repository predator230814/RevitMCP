using System.Text.Json;
using System.Text.Json.Serialization;

namespace BimIt.Revit.Assistant.Ipc;

public enum AssistantWorkflowName
{
    Context,
    Warnings,
    ActiveView,
}

public enum AssistantIpcKind
{
    Hello,
    RunWorkflow,
    Shutdown,
    Status,
    WorkflowResult,
}

public sealed record AssistantIpcMessage(
    AssistantIpcKind Kind,
    string? InstanceId,
    string? RevitVersion,
    string? RevitBuild,
    bool BridgeReady,
    string? RequestId,
    AssistantWorkflowName? Workflow,
    string? AssistantState,
    string? McpState,
    int? ToolCount,
    IReadOnlyList<string>? ToolNames,
    string? Error,
    string? Title,
    string? Body,
    bool IsError);

public readonly record struct AssistantIpcParse(bool Accepted, AssistantIpcMessage? Message, string Reason)
{
    public static AssistantIpcParse Accept(AssistantIpcMessage message) => new(true, message, "");

    public static AssistantIpcParse Reject(string reason) => new(false, null, reason);
}

public static class AssistantIpc
{
    public const int MaxMessageBytes = 262_144;

    public const int MaxToolNameCount = 64;

    public const int MaxTextLength = 16_000;

    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static AssistantIpcParse Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return AssistantIpcParse.Reject("empty");
        }

        if (System.Text.Encoding.UTF8.GetByteCount(json) > MaxMessageBytes)
        {
            return AssistantIpcParse.Reject("too-long");
        }

        try
        {
            var body = JsonSerializer.Deserialize<Envelope>(json, Options);
            if (body is null || string.IsNullOrWhiteSpace(body.Type))
            {
                return AssistantIpcParse.Reject("missing-type");
            }

            return body.Type switch
            {
                "hello" => ParseHello(body),
                "runWorkflow" => ParseRun(body),
                "shutdown" => AssistantIpcParse.Accept(new AssistantIpcMessage(
                    AssistantIpcKind.Shutdown, null, null, null, false, null, null, null, null, null, null, null, null, null, false)),
                "status" => ParseStatus(body),
                "workflowResult" => ParseResult(body),
                _ => AssistantIpcParse.Reject("unknown"),
            };
        }
        catch (JsonException)
        {
            return AssistantIpcParse.Reject("malformed");
        }
    }

    public static string Serialize(AssistantIpcMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var envelope = message.Kind switch
        {
            AssistantIpcKind.Hello => new Envelope
            {
                Type = "hello",
                InstanceId = message.InstanceId,
                RevitVersion = message.RevitVersion,
                RevitBuild = message.RevitBuild,
                BridgeReady = message.BridgeReady,
            },
            AssistantIpcKind.RunWorkflow => new Envelope
            {
                Type = "runWorkflow",
                RequestId = message.RequestId,
                Workflow = WireWorkflow(message.Workflow),
            },
            AssistantIpcKind.Shutdown => new Envelope { Type = "shutdown" },
            AssistantIpcKind.Status => new Envelope
            {
                Type = "status",
                AssistantState = message.AssistantState,
                McpState = message.McpState,
                ToolCount = message.ToolCount,
                ToolNames = message.ToolNames,
                Error = message.Error,
            },
            AssistantIpcKind.WorkflowResult => new Envelope
            {
                Type = "workflowResult",
                RequestId = message.RequestId,
                Title = message.Title,
                Body = message.Body,
                IsError = message.IsError,
            },
            _ => throw new ArgumentOutOfRangeException(nameof(message)),
        };
        return JsonSerializer.Serialize(envelope, Options);
    }

    public static bool GrantsApprovalAuthority => false;

    private static AssistantIpcParse ParseHello(Envelope body)
    {
        if (!IsBoundedId(body.InstanceId))
        {
            return AssistantIpcParse.Reject("instance");
        }

        return AssistantIpcParse.Accept(new AssistantIpcMessage(
            AssistantIpcKind.Hello,
            body.InstanceId,
            Bound(body.RevitVersion, 64),
            Bound(body.RevitBuild, 64),
            body.BridgeReady == true,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            false));
    }

    private static AssistantIpcParse ParseRun(Envelope body)
    {
        if (!IsBoundedId(body.RequestId) || !TryWorkflow(body.Workflow, out var workflow))
        {
            return AssistantIpcParse.Reject("workflow");
        }

        return AssistantIpcParse.Accept(new AssistantIpcMessage(
            AssistantIpcKind.RunWorkflow,
            null,
            null,
            null,
            false,
            body.RequestId,
            workflow,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            false));
    }

    private static AssistantIpcParse ParseStatus(Envelope body)
    {
        if (!IsState(body.AssistantState) || !IsState(body.McpState))
        {
            return AssistantIpcParse.Reject("status");
        }

        if (body.ToolNames is { Count: > MaxToolNameCount })
        {
            return AssistantIpcParse.Reject("tools");
        }

        return AssistantIpcParse.Accept(new AssistantIpcMessage(
            AssistantIpcKind.Status,
            null,
            null,
            null,
            false,
            null,
            null,
            body.AssistantState,
            body.McpState,
            body.ToolCount,
            body.ToolNames,
            Bound(body.Error, 2000),
            null,
            null,
            false));
    }

    private static AssistantIpcParse ParseResult(Envelope body)
    {
        if (!IsBoundedId(body.RequestId) || string.IsNullOrWhiteSpace(body.Title) || body.Body is null)
        {
            return AssistantIpcParse.Reject("result");
        }

        if (body.Title.Length > 200 || body.Body.Length > MaxTextLength)
        {
            return AssistantIpcParse.Reject("result");
        }

        return AssistantIpcParse.Accept(new AssistantIpcMessage(
            AssistantIpcKind.WorkflowResult,
            null,
            null,
            null,
            false,
            body.RequestId,
            null,
            null,
            null,
            null,
            null,
            null,
            body.Title,
            body.Body,
            body.IsError == true));
    }

    private static bool TryWorkflow(string? value, out AssistantWorkflowName workflow)
    {
        switch (value)
        {
            case "context":
                workflow = AssistantWorkflowName.Context;
                return true;
            case "warnings":
                workflow = AssistantWorkflowName.Warnings;
                return true;
            case "activeView":
                workflow = AssistantWorkflowName.ActiveView;
                return true;
            default:
                workflow = default;
                return false;
        }
    }

    private static string? WireWorkflow(AssistantWorkflowName? workflow)
    {
        return workflow switch
        {
            AssistantWorkflowName.Context => "context",
            AssistantWorkflowName.Warnings => "warnings",
            AssistantWorkflowName.ActiveView => "activeView",
            _ => null,
        };
    }

    private static bool IsBoundedId(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) && value.Length <= 128 && value.All(character => !char.IsWhiteSpace(character));
    }

    private static bool IsState(string? value)
    {
        return value is "starting" or "ready" or "unavailable";
    }

    private static string? Bound(string? value, int max)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        return value.Length <= max ? value : value[..max];
    }

    private sealed class Envelope
    {
        public string? Type { get; set; }

        public string? InstanceId { get; set; }

        public string? RevitVersion { get; set; }

        public string? RevitBuild { get; set; }

        public bool? BridgeReady { get; set; }

        public string? RequestId { get; set; }

        public string? Workflow { get; set; }

        public string? AssistantState { get; set; }

        public string? McpState { get; set; }

        public int? ToolCount { get; set; }

        public IReadOnlyList<string>? ToolNames { get; set; }

        public string? Error { get; set; }

        public string? Title { get; set; }

        public string? Body { get; set; }

        public bool? IsError { get; set; }
    }
}
