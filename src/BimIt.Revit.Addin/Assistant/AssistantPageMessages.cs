using System.Text.Json;
using System.Text.Json.Serialization;

namespace BimIt.Revit.Addin.Assistant;

internal enum AssistantPageIntent
{
    PageReady,
    RunQuickAction,
    SendCommand,
    NewConversation,
    OpenDiagnostics,
    RetryConnection,
    AttachDocument,
    RemoveDocument,
    ConfirmProjectAxesPreview,
    ReviewInRevit,
    ApplyApprovedPlacements,
    PreparePatternPreview,
    ReviewPatternInRevit,
    ApplyApprovedPattern,
    SearchPatternTypes,
    Skills,
}

internal readonly record struct AssistantPageParse(bool Accepted, AssistantPageIntent Intent, string? Action, string? Text, string Reason)
{
    public static AssistantPageParse Accept(AssistantPageIntent intent, string? action = null, string? text = null)
        => new(true, intent, action, text, "");

    public static AssistantPageParse Reject(string reason) => new(false, default, null, null, reason);
}

internal static class AssistantPageMessages
{
    public const int MaxCommandLength = 2_000;

    public static readonly string[] QuickActions = ["context", "warnings", "activeView"];

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static bool GrantsApprovalAuthority => false;

    public static AssistantPageParse Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return AssistantPageParse.Reject("empty");
        }

        if (json.Length > 8_000)
        {
            return AssistantPageParse.Reject("too-long");
        }

        try
        {
            var message = JsonSerializer.Deserialize<Envelope>(json, Options);
            if (message is null || string.IsNullOrWhiteSpace(message.Type))
            {
                return AssistantPageParse.Reject("missing-type");
            }

            return message.Type switch
            {
                "pageReady" => AssistantPageParse.Accept(AssistantPageIntent.PageReady),
                "runQuickAction" => ParseAction(message.Action),
                "sendCommand" => ParseCommand(message.Text),
                "newConversation" => AssistantPageParse.Accept(AssistantPageIntent.NewConversation),
                "openDiagnostics" => AssistantPageParse.Accept(AssistantPageIntent.OpenDiagnostics),
                "retryConnection" => AssistantPageParse.Accept(AssistantPageIntent.RetryConnection),
                "attachDocument" => AssistantPageParse.Accept(AssistantPageIntent.AttachDocument),
                "removeDocument" => AssistantPageParse.Accept(AssistantPageIntent.RemoveDocument),
                "confirmProjectAxesPreview" => message.Text is null && message.Action is null
                    ? AssistantPageParse.Accept(AssistantPageIntent.ConfirmProjectAxesPreview)
                    : AssistantPageParse.Reject("confirm"),
                "reviewInRevit" => message.Text is null && message.Action is null
                    ? AssistantPageParse.Accept(AssistantPageIntent.ReviewInRevit)
                    : AssistantPageParse.Reject("review"),
                "applyApprovedPlacements" => message.Text is null && message.Action is null
                    ? AssistantPageParse.Accept(AssistantPageIntent.ApplyApprovedPlacements)
                    : AssistantPageParse.Reject("apply"),
                "preparePatternPreview" => ParsePatternConfiguration(message),
                "reviewPatternInRevit" => message.Text is null && message.Action is null
                    ? AssistantPageParse.Accept(AssistantPageIntent.ReviewPatternInRevit)
                    : AssistantPageParse.Reject("pattern-review"),
                "applyApprovedPattern" => message.Text is null && message.Action is null
                    ? AssistantPageParse.Accept(AssistantPageIntent.ApplyApprovedPattern)
                    : AssistantPageParse.Reject("pattern-apply"),
                "searchPatternTypes" => ParseTypeSearch(message),
                "skills" => ParseSkills(message),
                _ => AssistantPageParse.Reject("unknown"),
            };
        }
        catch (JsonException)
        {
            return AssistantPageParse.Reject("malformed");
        }
    }

    private static AssistantPageParse ParseAction(string? action)
    {
        if (action is null || !QuickActions.Contains(action, StringComparer.Ordinal))
        {
            return AssistantPageParse.Reject("action");
        }

        return AssistantPageParse.Accept(AssistantPageIntent.RunQuickAction, action);
    }

    private static AssistantPageParse ParsePatternConfiguration(Envelope message)
    {
        if (message.Action is not null || string.IsNullOrWhiteSpace(message.Text) || message.Text.Length > MaxCommandLength)
        {
            return AssistantPageParse.Reject("pattern-preview");
        }

        return AssistantPageParse.Accept(AssistantPageIntent.PreparePatternPreview, text: message.Text);
    }

    private static AssistantPageParse ParseTypeSearch(Envelope message)
    {
        if (message.Action is null || message.Text is null)
        {
            return AssistantPageParse.Reject("pattern-types");
        }

        var request = BimIt.Revit.Assistant.Ipc.RoomPatternTypeSearch.SerializeRequest(message.Action, message.Text);
        return BimIt.Revit.Assistant.Ipc.RoomPatternTypeSearch.TryParseRequest(request, out _, out _)
            ? AssistantPageParse.Accept(AssistantPageIntent.SearchPatternTypes, message.Action, message.Text.Trim())
            : AssistantPageParse.Reject("pattern-types");
    }

    /// <summary>
    /// Settings → Skills. The page names an action and, where needed, an opaque Skill key or source id. It can never
    /// supply a path: addSource and import open a native folder picker in the add-in.
    /// </summary>
    private static AssistantPageParse ParseSkills(Envelope message)
    {
        var action = message.Action;
        if (action is null || !SkillPageActions.Contains(action, StringComparer.Ordinal))
        {
            return AssistantPageParse.Reject("skills");
        }

        var request = new BimIt.Revit.Assistant.Ipc.AssistantSkillsIpc.Request { Action = action };
        switch (action)
        {
            case "detail":
            case "enable":
            case "disable":
                request.Key = message.Text;
                break;
            case "removeSource":
                request.SourceId = message.Text;
                break;
            case "addSource":
            case "import":
                if (message.Text is not null)
                {
                    return AssistantPageParse.Reject("skills");
                }

                // The add-in supplies the path after the folder picker. Validate the shape with a placeholder.
                request.Path = @"C:\placeholder";
                break;
            default:
                if (message.Text is not null)
                {
                    return AssistantPageParse.Reject("skills");
                }

                break;
        }

        var json = BimIt.Revit.Assistant.Ipc.AssistantSkillsIpc.SerializeRequest(request);
        return BimIt.Revit.Assistant.Ipc.AssistantSkillsIpc.TryParseRequest(json, out _)
            ? AssistantPageParse.Accept(AssistantPageIntent.Skills, action, message.Text)
            : AssistantPageParse.Reject("skills");
    }

    public static readonly string[] SkillPageActions =
        ["list", "reload", "detail", "enable", "disable", "openCustomFolder", "addSource", "removeSource", "import"];

    private static AssistantPageParse ParseCommand(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaxCommandLength)
        {
            return AssistantPageParse.Reject("text");
        }

        return AssistantPageParse.Accept(AssistantPageIntent.SendCommand, text: text);
    }

    private sealed class Envelope
    {
        public string? Type { get; set; }

        public string? Action { get; set; }

        public string? Text { get; set; }
    }
}
