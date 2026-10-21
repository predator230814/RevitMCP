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
    CalibratePlanPreview,
    SubmitCalibrationPoints,
    CancelCalibration,
}

internal readonly record struct AssistantPageParse(
    bool Accepted,
    AssistantPageIntent Intent,
    string? Action,
    string? Text,
    string Reason,
    string? SessionRef = null,
    double? SourceAX = null,
    double? SourceAY = null,
    double? SourceBX = null,
    double? SourceBY = null)
{
    public static AssistantPageParse Accept(AssistantPageIntent intent, string? action = null, string? text = null)
        => new(true, intent, action, text, "");

    public static AssistantPageParse AcceptPoints(string sessionRef, double sourceAX, double sourceAY, double sourceBX, double sourceBY)
        => new(true, AssistantPageIntent.SubmitCalibrationPoints, null, null, "", sessionRef, sourceAX, sourceAY, sourceBX, sourceBY);

    public static AssistantPageParse AcceptSession(AssistantPageIntent intent, string sessionRef)
        => new(true, intent, null, null, "", sessionRef);

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
                "confirmProjectAxesPreview" => message.Text is null && message.Action is null && message.SessionRef is null
                    ? AssistantPageParse.Accept(AssistantPageIntent.ConfirmProjectAxesPreview)
                    : AssistantPageParse.Reject("confirm"),
                "calibratePlanPreview" => message.Text is null && message.Action is null && message.SessionRef is null
                    ? AssistantPageParse.Accept(AssistantPageIntent.CalibratePlanPreview)
                    : AssistantPageParse.Reject("calibrate"),
                "submitCalibrationPoints" => ParsePoints(message),
                "cancelCalibration" => IsSession(message.SessionRef) && message.Text is null && message.Action is null && message.SourceAX is null
                    ? AssistantPageParse.AcceptSession(AssistantPageIntent.CancelCalibration, message.SessionRef!)
                    : AssistantPageParse.Reject("calibrate"),
                "reviewInRevit" => message.Text is null && message.Action is null && message.SessionRef is null
                    ? AssistantPageParse.Accept(AssistantPageIntent.ReviewInRevit)
                    : AssistantPageParse.Reject("review"),
                "applyApprovedPlacements" => message.Text is null && message.Action is null && message.SessionRef is null
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

    private static AssistantPageParse ParsePoints(Envelope message)
    {
        if (message.Text is not null
            || message.Action is not null
            || !IsSession(message.SessionRef)
            || !IsUnit(message.SourceAX)
            || !IsUnit(message.SourceAY)
            || !IsUnit(message.SourceBX)
            || !IsUnit(message.SourceBY))
        {
            return AssistantPageParse.Reject("calibrate");
        }

        return AssistantPageParse.AcceptPoints(
            message.SessionRef!,
            message.SourceAX!.Value,
            message.SourceAY!.Value,
            message.SourceBX!.Value,
            message.SourceBY!.Value);
    }

    private static bool IsSession(string? value)
    {
        return value is { Length: 32 } && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
    }

    private static bool IsUnit(double? value)
    {
        return value is double number && double.IsFinite(number) && number >= 0d && number <= 1d;
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

        public string? SessionRef { get; set; }

        public double? SourceAX { get; set; }

        public double? SourceAY { get; set; }

        public double? SourceBX { get; set; }

        public double? SourceBY { get; set; }
    }
}
