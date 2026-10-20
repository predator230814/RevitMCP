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
