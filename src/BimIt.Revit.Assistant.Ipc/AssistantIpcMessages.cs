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
    Chat,
    ChatResult,
    ResetConversation,
    QueryStatus,
    AttachDocument,
    RemoveDocument,
    DocumentResult,
    ConfirmProjectAxesPreview,
    PreviewResult,
    ReviewInRevit,
    ApplyApprovedPlacements,
    PreparePatternPreview,
    ReviewPatternInRevit,
    ApplyApprovedPattern,
    SearchPatternTypes,
    PatternTypesResult,
    Skills,
    SkillsResult,
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
    bool IsError,
    string? ProviderName = null,
    string? ProviderState = null,
    string? ProviderModel = null,
    string? ProviderError = null,
    string? ProviderLabel = null,
    string? Text = null,
    IReadOnlyList<string>? UsedTools = null,
    string? DocumentPath = null,
    bool? Attached = null,
    string? DisplayName = null,
    string? DocumentKind = null,
    long? SourceSizeBytes = null,
    int? PageCount = null,
    int? VisualCount = null,
    bool? Truncated = null,
    string? DocumentCode = null,
    string? DocumentMessage = null,
    string? PatternReview = null);

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

    public const int MaxChatTextLength = 2_000;

    public const int MaxUsedToolCount = 8;

    public const int MaxDocumentPathLength = 1_024;

    public const long MaxDocumentBytes = 25L * 1024 * 1024;

    public const int MaxDocumentNameLength = 180;

    public const int MaxDocumentMessageLength = 500;

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

            if (body.PatternReview is not null && body.Type != "chatResult")
            {
                return AssistantIpcParse.Reject("pattern");
            }

            return body.Type switch
            {
                "hello" => ParseHello(body),
                "runWorkflow" => ParseRun(body),
                "shutdown" => AssistantIpcParse.Accept(new AssistantIpcMessage(
                    AssistantIpcKind.Shutdown, null, null, null, false, null, null, null, null, null, null, null, null, null, false)),
                "status" => ParseStatus(body),
                "workflowResult" => ParseResult(body),
                "chat" => ParseChat(body),
                "chatResult" => ParseChatResult(body),
                "resetConversation" => ParseSignal(body, AssistantIpcKind.ResetConversation),
                "queryStatus" => ParseSignal(body, AssistantIpcKind.QueryStatus),
                "attachDocument" => ParseAttach(body),
                "removeDocument" => ParseSignal(body, AssistantIpcKind.RemoveDocument),
                "documentResult" => ParseDocumentResult(body),
                "confirmProjectAxesPreview" => ParseSignal(body, AssistantIpcKind.ConfirmProjectAxesPreview),
                "reviewInRevit" => ParseSignal(body, AssistantIpcKind.ReviewInRevit),
                "applyApprovedPlacements" => ParseSignal(body, AssistantIpcKind.ApplyApprovedPlacements),
                "preparePatternPreview" => ParsePreparePattern(body),
                "reviewPatternInRevit" => ParseSignal(body, AssistantIpcKind.ReviewPatternInRevit),
                "applyApprovedPattern" => ParseSignal(body, AssistantIpcKind.ApplyApprovedPattern),
                "previewResult" => ParsePreviewResult(body),
                "searchPatternTypes" => ParseSearchPatternTypes(body),
                "patternTypesResult" => ParsePatternTypesResult(body),
                "skills" => ParseSkills(body),
                "skillsResult" => ParseSkillsResult(body),
                _ =>AssistantIpcParse.Reject("unknown"),
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
                ProviderName = message.ProviderName,
                ProviderState = message.ProviderState,
                ProviderModel = message.ProviderModel,
                ProviderError = message.ProviderError,
                ProviderLabel = message.ProviderLabel,
            },
            AssistantIpcKind.WorkflowResult => new Envelope
            {
                Type = "workflowResult",
                RequestId = message.RequestId,
                Title = message.Title,
                Body = message.Body,
                IsError = message.IsError,
            },
            AssistantIpcKind.Chat => new Envelope
            {
                Type = "chat",
                RequestId = message.RequestId,
                Text = message.Text,
            },
            AssistantIpcKind.ChatResult => new Envelope
            {
                Type = "chatResult",
                RequestId = message.RequestId,
                Title = message.Title,
                Body = message.Body,
                IsError = message.IsError,
                ProviderName = message.ProviderName,
                ProviderState = message.ProviderState,
                ProviderModel = message.ProviderModel,
                ProviderError = message.ProviderError,
                ProviderLabel = message.ProviderLabel,
                UsedTools = message.UsedTools ?? [],
                PatternReview = message.PatternReview,
            },
            AssistantIpcKind.ResetConversation => new Envelope { Type = "resetConversation" },
            AssistantIpcKind.QueryStatus => new Envelope { Type = "queryStatus" },
            AssistantIpcKind.AttachDocument => new Envelope
            {
                Type = "attachDocument",
                Path = message.DocumentPath,
            },
            AssistantIpcKind.RemoveDocument => new Envelope { Type = "removeDocument" },
            AssistantIpcKind.ConfirmProjectAxesPreview => new Envelope { Type = "confirmProjectAxesPreview" },
            AssistantIpcKind.ReviewInRevit => new Envelope { Type = "reviewInRevit" },
            AssistantIpcKind.ApplyApprovedPlacements => new Envelope { Type = "applyApprovedPlacements" },
            AssistantIpcKind.PreparePatternPreview => new Envelope
            {
                Type = "preparePatternPreview",
                Text = message.Text,
            },
            AssistantIpcKind.ReviewPatternInRevit => new Envelope { Type = "reviewPatternInRevit" },
            AssistantIpcKind.ApplyApprovedPattern => new Envelope { Type = "applyApprovedPattern" },
            AssistantIpcKind.PreviewResult => new Envelope
            {
                Type = "previewResult",
                Title = message.Title,
                Body = message.Body,
                IsError = message.IsError,
            },
            AssistantIpcKind.SearchPatternTypes => new Envelope
            {
                Type = "searchPatternTypes",
                Text = message.Text,
            },
            AssistantIpcKind.PatternTypesResult => new Envelope
            {
                Type = "patternTypesResult",
                Title = message.Title,
                Body = message.Body,
                IsError = message.IsError,
                Text = message.Text,
            },
            AssistantIpcKind.Skills => new Envelope
            {
                Type = "skills",
                Text = message.Text,
            },
            AssistantIpcKind.SkillsResult => new Envelope
            {
                Type = "skillsResult",
                IsError = message.IsError,
                Text = message.Text,
            },
            AssistantIpcKind.DocumentResult => new Envelope
            {
                Type = "documentResult",
                Attached = message.Attached,
                DisplayName = message.DisplayName,
                DocumentKind = message.DocumentKind,
                SourceSizeBytes = message.SourceSizeBytes,
                PageCount = message.PageCount,
                VisualCount = message.VisualCount,
                Truncated = message.Truncated,
                Code = message.DocumentCode,
                DocumentMessage = message.DocumentMessage,
            },
            _ => throw new ArgumentOutOfRangeException(nameof(message)),
        };
        return JsonSerializer.Serialize(envelope, Options);
    }

    public static bool GrantsApprovalAuthority => false;

    public static bool IsModelToolName(string? name)
    {
        return name is "bimit_get_model_context"
            or "bimit_get_warnings"
            or "bimit_get_active_view_elements"
            or "bimit_inspect_selection"
            or "bimit_explore_selected_mep"
            or "bimit_propose_mep_device_placements"
            or "bimit_propose_room_pattern"
            or "bimit_read_skill";
    }

    private static AssistantIpcParse ParseHello(Envelope body)
    {
        if (!IsBoundedId(body.InstanceId) || body.UsedTools is not null || HasDocument(body))
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
        if (!IsBoundedId(body.RequestId) || !TryWorkflow(body.Workflow, out var workflow) || body.UsedTools is not null || HasDocument(body))
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
        if (body.UsedTools is not null
            || HasDocument(body)
            || !IsState(body.AssistantState)
            || !IsState(body.McpState)
            || !IsProviderState(body.ProviderState)
            || !IsModelToken(body.ProviderModel))
        {
            return AssistantIpcParse.Reject("status");
        }

        if (body.ProviderName is { Length: > 64 } || body.ProviderLabel is { Length: > 160 })
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
            false,
            Bound(body.ProviderName, 64),
            body.ProviderState,
            body.ProviderModel,
            Bound(body.ProviderError, 500),
            Bound(body.ProviderLabel, 160),
            null));
    }

    private static AssistantIpcParse ParseChat(Envelope body)
    {
        if (HasProvider(body) || HasDocument(body) || body.UsedTools is not null || body.Workflow is not null || body.Title is not null || body.Body is not null)
        {
            return AssistantIpcParse.Reject("chat");
        }

        if (!IsBoundedId(body.RequestId) || string.IsNullOrWhiteSpace(body.Text) || body.Text.Length > MaxChatTextLength)
        {
            return AssistantIpcParse.Reject("chat");
        }

        return AssistantIpcParse.Accept(new AssistantIpcMessage(
            AssistantIpcKind.Chat,
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
            null,
            null,
            false,
            Text: body.Text));
    }

    private static AssistantIpcParse ParseChatResult(Envelope body)
    {
        if (!IsBoundedId(body.RequestId) || string.IsNullOrWhiteSpace(body.Title) || body.Body is null)
        {
            return AssistantIpcParse.Reject("chat");
        }

        if (body.Title.Length > 200 || body.Body.Length > MaxTextLength || body.Text is not null || body.Workflow is not null || HasDocument(body))
        {
            return AssistantIpcParse.Reject("chat");
        }

        if (!IsProviderState(body.ProviderState) || !IsModelToken(body.ProviderModel))
        {
            return AssistantIpcParse.Reject("chat");
        }

        if (body.ProviderName is { Length: > 64 } || body.ProviderLabel is { Length: > 160 } || !TryUsedTools(body.UsedTools, out var usedTools))
        {
            return AssistantIpcParse.Reject("chat");
        }

        string? patternReview = null;
        if (body.PatternReview is not null && !RoomPatternReview.TryParse(body.PatternReview, out _))
        {
            return AssistantIpcParse.Reject("pattern");
        }

        patternReview = body.PatternReview;

        return AssistantIpcParse.Accept(new AssistantIpcMessage(
            AssistantIpcKind.ChatResult,
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
            body.IsError == true,
            Bound(body.ProviderName, 64),
            body.ProviderState,
            body.ProviderModel,
            Bound(body.ProviderError, 500),
            Bound(body.ProviderLabel, 160),
            null,
            usedTools,
            PatternReview: patternReview));
    }

    private static AssistantIpcParse ParsePreparePattern(Envelope body)
    {
        if (body.RequestId is not null
            || body.Workflow is not null
            || body.Title is not null
            || body.Body is not null
            || HasProvider(body)
            || body.UsedTools is not null
            || body.InstanceId is not null
            || body.PatternReview is not null
            || HasDocument(body)
            || string.IsNullOrWhiteSpace(body.Text)
            || body.Text.Length > MaxTextLength)
        {
            return AssistantIpcParse.Reject("pattern-preview");
        }

        return AssistantIpcParse.Accept(new AssistantIpcMessage(
            AssistantIpcKind.PreparePatternPreview,
            null,
            null,
            null,
            false,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            false,
            Text: body.Text));
    }

    private static AssistantIpcParse ParseSearchPatternTypes(Envelope body)
    {
        if (body.RequestId is not null
            || body.Workflow is not null
            || body.Title is not null
            || body.Body is not null
            || HasProvider(body)
            || body.UsedTools is not null
            || body.InstanceId is not null
            || body.PatternReview is not null
            || HasDocument(body)
            || !RoomPatternTypeSearch.TryParseRequest(body.Text, out _, out _))
        {
            return AssistantIpcParse.Reject("pattern-types");
        }

        return AssistantIpcParse.Accept(new AssistantIpcMessage(
            AssistantIpcKind.SearchPatternTypes, null, null, null, false, null, null, null, null, null, null, null, null, null, false, Text: body.Text));
    }

    private static AssistantIpcParse ParsePatternTypesResult(Envelope body)
    {
        if (string.IsNullOrWhiteSpace(body.Title)
            || body.Body is null
            || body.Title.Length > 200
            || body.Body.Length > MaxTextLength
            || body.RequestId is not null
            || body.Workflow is not null
            || HasProvider(body)
            || body.UsedTools is not null
            || body.InstanceId is not null
            || HasDocument(body))
        {
            return AssistantIpcParse.Reject("pattern-types");
        }

        var isError = body.IsError == true;
        if (isError ? body.Text is not null : !RoomPatternTypeSearch.TryParseResult(body.Text, out _))
        {
            return AssistantIpcParse.Reject("pattern-types");
        }

        return AssistantIpcParse.Accept(new AssistantIpcMessage(
            AssistantIpcKind.PatternTypesResult, null, null, null, false, null, null, null, null, null, null, null, body.Title, body.Body, isError, Text: body.Text));
    }

    private static AssistantIpcParse ParseSkills(Envelope body)
    {
        if (body.RequestId is not null
            || body.Workflow is not null
            || body.Title is not null
            || body.Body is not null
            || body.IsError is not null
            || HasProvider(body)
            || body.UsedTools is not null
            || body.InstanceId is not null
            || body.PatternReview is not null
            || HasDocument(body)
            || !AssistantSkillsIpc.TryParseRequest(body.Text, out _))
        {
            return AssistantIpcParse.Reject("skills");
        }

        return AssistantIpcParse.Accept(new AssistantIpcMessage(
            AssistantIpcKind.Skills, null, null, null, false, null, null, null, null, null, null, null, null, null, false, Text: body.Text));
    }

    private static AssistantIpcParse ParseSkillsResult(Envelope body)
    {
        if (body.RequestId is not null
            || body.Workflow is not null
            || body.Title is not null
            || body.Body is not null
            || HasProvider(body)
            || body.UsedTools is not null
            || body.InstanceId is not null
            || body.PatternReview is not null
            || HasDocument(body)
            || !AssistantSkillsIpc.TryParseView(body.Text, out _))
        {
            return AssistantIpcParse.Reject("skills");
        }

        return AssistantIpcParse.Accept(new AssistantIpcMessage(
            AssistantIpcKind.SkillsResult, null, null, null, false, null, null, null, null, null, null, null, null, null, body.IsError == true, Text: body.Text));
    }

    private static AssistantIpcParse ParseSignal(Envelope body, AssistantIpcKind kind)
    {
        if (body.RequestId is not null
            || body.Text is not null
            || body.Workflow is not null
            || body.Title is not null
            || body.Body is not null
            || HasProvider(body)
            || body.UsedTools is not null
            || body.InstanceId is not null
            || body.PatternReview is not null
            || HasDocument(body))
        {
            return AssistantIpcParse.Reject("signal");
        }

        return AssistantIpcParse.Accept(new AssistantIpcMessage(
            kind, null, null, null, false, null, null, null, null, null, null, null, null, null, false));
    }

    private static AssistantIpcParse ParsePreviewResult(Envelope body)
    {
        if (string.IsNullOrWhiteSpace(body.Title) || body.Body is null || body.RequestId is not null || body.UsedTools is not null || HasDocument(body))
        {
            return AssistantIpcParse.Reject("preview");
        }

        if (body.Title.Length > 200 || body.Body.Length > MaxTextLength)
        {
            return AssistantIpcParse.Reject("preview");
        }

        return AssistantIpcParse.Accept(new AssistantIpcMessage(
            AssistantIpcKind.PreviewResult,
            null,
            null,
            null,
            false,
            null,
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

    private static AssistantIpcParse ParseResult(Envelope body)
    {
        if (!IsBoundedId(body.RequestId) || string.IsNullOrWhiteSpace(body.Title) || body.Body is null)
        {
            return AssistantIpcParse.Reject("result");
        }

        if (body.Title.Length > 200 || body.Body.Length > MaxTextLength || body.UsedTools is not null || HasDocument(body))
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

    private static AssistantIpcParse ParseAttach(Envelope body)
    {
        if (!IsAcceptedPath(body.Path)
            || HasDocumentMetadata(body)
            || body.RequestId is not null
            || body.Text is not null
            || body.Workflow is not null
            || body.InstanceId is not null
            || body.UsedTools is not null
            || HasProvider(body))
        {
            return AssistantIpcParse.Reject("path");
        }

        return AssistantIpcParse.Accept(new AssistantIpcMessage(
            AssistantIpcKind.AttachDocument,
            null,
            null,
            null,
            false,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            false,
            DocumentPath: body.Path));
    }

    private static AssistantIpcParse ParseDocumentResult(Envelope body)
    {
        if (body.Path is not null
            || body.RequestId is not null
            || body.Text is not null
            || body.Workflow is not null
            || body.Title is not null
            || body.Body is not null
            || body.UsedTools is not null
            || HasProvider(body)
            || body.InstanceId is not null
            || body.Attached is null)
        {
            return AssistantIpcParse.Reject("document");
        }

        if (body.Attached == true)
        {
            if (!IsDisplayName(body.DisplayName)
                || !IsDocumentKind(body.DocumentKind)
                || body.SourceSizeBytes is null
                || body.SourceSizeBytes < 0
                || body.SourceSizeBytes > MaxDocumentBytes
                || body.Code is not null
                || body.DocumentMessage is not null
                || !IsOptionalCount(body.PageCount, 100_000)
                || !IsOptionalCount(body.VisualCount, 8))
            {
                return AssistantIpcParse.Reject("document");
            }

            return AssistantIpcParse.Accept(new AssistantIpcMessage(
                AssistantIpcKind.DocumentResult,
                null,
                null,
                null,
                false,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                false,
                Attached: true,
                DisplayName: body.DisplayName,
                DocumentKind: body.DocumentKind,
                SourceSizeBytes: body.SourceSizeBytes,
                PageCount: body.PageCount,
                VisualCount: body.VisualCount,
                Truncated: body.Truncated == true));
        }

        if (body.DisplayName is not null
            || body.DocumentKind is not null
            || body.SourceSizeBytes is not null
            || body.PageCount is not null
            || body.VisualCount is not null
            || body.Truncated is not null)
        {
            return AssistantIpcParse.Reject("document");
        }

        if (body.Code is null && body.DocumentMessage is null)
        {
            return AssistantIpcParse.Accept(new AssistantIpcMessage(
                AssistantIpcKind.DocumentResult,
                null,
                null,
                null,
                false,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                false,
                Attached: false));
        }

        if (!IsDocumentCode(body.Code)
            || string.IsNullOrWhiteSpace(body.DocumentMessage)
            || body.DocumentMessage.Length > MaxDocumentMessageLength
            || ContainsPath(body.DocumentMessage))
        {
            return AssistantIpcParse.Reject("document");
        }

        return AssistantIpcParse.Accept(new AssistantIpcMessage(
            AssistantIpcKind.DocumentResult,
            null,
            null,
            null,
            false,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            false,
            Attached: false,
            DocumentCode: body.Code,
            DocumentMessage: body.DocumentMessage));
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

    private static bool TryUsedTools(IReadOnlyList<string>? names, out IReadOnlyList<string> used)
    {
        if (names is null)
        {
            used = [];
            return true;
        }

        if (names.Count > MaxUsedToolCount || names.Any(name => !IsModelToolName(name)))
        {
            used = [];
            return false;
        }

        used = names;
        return true;
    }

    private static bool IsBoundedId(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) && value.Length <= 128 && value.All(character => !char.IsWhiteSpace(character));
    }

    private static bool IsState(string? value)
    {
        return value is "starting" or "ready" or "unavailable";
    }

    private static bool IsProviderState(string? value)
    {
        return value is null or "not_configured" or "starting" or "ready" or "unavailable";
    }

    private static bool IsModelToken(string? value)
    {
        return string.IsNullOrEmpty(value)
            || (value.Length <= 80 && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or ':' or '-'));
    }

    private static bool HasDocument(Envelope body)
    {
        return body.Path is not null || HasDocumentMetadata(body);
    }

    private static bool HasDocumentMetadata(Envelope body)
    {
        return body.Attached is not null
            || body.DisplayName is not null
            || body.DocumentKind is not null
            || body.SourceSizeBytes is not null
            || body.PageCount is not null
            || body.VisualCount is not null
            || body.Truncated is not null
            || body.Code is not null
            || body.DocumentMessage is not null;
    }

    private static bool IsAcceptedPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > MaxDocumentPathLength || path.Contains("://", StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var character in path)
        {
            if (char.IsControl(character))
            {
                return false;
            }
        }

        return path.IndexOfAny(Path.GetInvalidPathChars()) < 0 && Path.IsPathRooted(path);
    }

    private static bool IsDisplayName(string? name)
    {
        return !string.IsNullOrWhiteSpace(name)
            && name.Length <= MaxDocumentNameLength
            && !name.Contains('\\')
            && !name.Contains('/')
            && !ContainsPath(name)
            && name.All(character => !char.IsControl(character));
    }

    private static bool IsDocumentKind(string? kind)
    {
        return kind is "pdf" or "image" or "word" or "spreadsheet" or "text" or "markdown" or "csv" or "json";
    }

    private static bool IsDocumentCode(string? code)
    {
        return code is "unsupported_document"
            or "document_too_large"
            or "document_invalid"
            or "document_read_failed"
            or "pdf_render_failed";
    }

    private static bool IsOptionalCount(int? value, int max)
    {
        return value is null || (value >= 0 && value <= max);
    }

    private static bool ContainsPath(string? value)
    {
        return value is not null
            && (value.Contains(":\\", StringComparison.Ordinal)
                || value.Contains("\\\\", StringComparison.Ordinal)
                || value.Contains("://", StringComparison.Ordinal));
    }

    private static bool HasProvider(Envelope body)
    {
        return body.ProviderName is not null
            || body.ProviderState is not null
            || body.ProviderModel is not null
            || body.ProviderError is not null
            || body.ProviderLabel is not null;
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

        public string? ProviderName { get; set; }

        public string? ProviderState { get; set; }

        public string? ProviderModel { get; set; }

        public string? ProviderError { get; set; }

        public string? ProviderLabel { get; set; }

        public string? Text { get; set; }

        public IReadOnlyList<string>? UsedTools { get; set; }

        public string? Path { get; set; }

        public bool? Attached { get; set; }

        public string? DisplayName { get; set; }

        [JsonPropertyName("kind")]
        public string? DocumentKind { get; set; }

        public long? SourceSizeBytes { get; set; }

        public int? PageCount { get; set; }

        public int? VisualCount { get; set; }

        public bool? Truncated { get; set; }

        [JsonPropertyName("code")]
        public string? Code { get; set; }

        [JsonPropertyName("message")]
        public string? DocumentMessage { get; set; }

        public string? PatternReview { get; set; }
    }
}
