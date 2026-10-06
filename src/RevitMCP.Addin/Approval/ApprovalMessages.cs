using System.Text.Json;
using System.Text.Json.Serialization;

namespace RevitMCP.Addin.Approval;

internal enum ApprovalInboundKind
{
    ApproveCurrent,
    RejectCurrent,
    DismissCurrent,
}

internal readonly record struct ApprovalInboundParse(bool Accepted, ApprovalInboundKind Kind, string? SessionRef, string Reason)
{
    public static ApprovalInboundParse Accept(ApprovalInboundKind kind, string sessionRef) => new(true, kind, sessionRef, "");

    public static ApprovalInboundParse Reject(string reason) => new(false, default, null, reason);
}

internal static class ApprovalMessageParser
{
    public const int MaxSessionRefLength = 128;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static ApprovalInboundParse Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return ApprovalInboundParse.Reject("empty");
        }

        try
        {
            var message = JsonSerializer.Deserialize<ApprovalInboundMessage>(json, Options);
            if (message is null || string.IsNullOrWhiteSpace(message.Type))
            {
                return ApprovalInboundParse.Reject("missing-type");
            }

            if (!TryKind(message.Type, out var kind))
            {
                return ApprovalInboundParse.Reject("unknown");
            }

            if (!IsBoundedSessionRef(message.SessionRef))
            {
                return ApprovalInboundParse.Reject("session");
            }

            return ApprovalInboundParse.Accept(kind, message.SessionRef!);
        }
        catch (JsonException)
        {
            return ApprovalInboundParse.Reject("malformed");
        }
    }

    private static bool TryKind(string type, out ApprovalInboundKind kind)
    {
        switch (type)
        {
            case "approveCurrent":
                kind = ApprovalInboundKind.ApproveCurrent;
                return true;
            case "rejectCurrent":
                kind = ApprovalInboundKind.RejectCurrent;
                return true;
            case "dismissCurrent":
                kind = ApprovalInboundKind.DismissCurrent;
                return true;
            default:
                kind = default;
                return false;
        }
    }

    private static bool IsBoundedSessionRef(string? sessionRef)
    {
        if (string.IsNullOrWhiteSpace(sessionRef) || sessionRef.Length > MaxSessionRefLength)
        {
            return false;
        }

        foreach (var character in sessionRef)
        {
            if (char.IsWhiteSpace(character))
            {
                return false;
            }
        }

        return true;
    }

    private sealed class ApprovalInboundMessage
    {
        public string? Type { get; set; }

        public string? SessionRef { get; set; }
    }
}

internal enum ApprovalUiDispatchKind
{
    Recorded,
    Rejected,
    Dismissed,
    Unavailable,
    InvalidSession,
}

internal readonly record struct ApprovalUiDispatchResult(ApprovalUiDispatchKind Kind, bool ReviewRemains)
{
    public static ApprovalUiDispatchResult Recorded() => new(ApprovalUiDispatchKind.Recorded, false);

    public static ApprovalUiDispatchResult Rejected() => new(ApprovalUiDispatchKind.Rejected, true);

    public static ApprovalUiDispatchResult Dismissed() => new(ApprovalUiDispatchKind.Dismissed, false);

    public static ApprovalUiDispatchResult Unavailable(bool reviewRemains) => new(ApprovalUiDispatchKind.Unavailable, reviewRemains);

    public static ApprovalUiDispatchResult InvalidSession() => new(ApprovalUiDispatchKind.InvalidSession, false);
}

internal readonly record struct ApprovalPresentationResult(ApprovalReviewStatus Status, string? SessionRef);

/// <summary>
/// Serializes the authoritative render model for the local page.
/// The payload is presentation only. It does not carry approval authority.
/// </summary>
internal static class ApprovalReviewJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = null,
    };

    public static string SerializeRender(ApprovalReviewRenderModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var items = new Dictionary<string, object?>[model.Items.Count];
        for (var index = 0; index < model.Items.Count; index++)
        {
            var item = model.Items[index];
            items[index] = new Dictionary<string, object?>
            {
                ["requestPosition"] = item.RequestPosition,
                ["elementName"] = item.ElementName,
                ["elementNameTruncated"] = item.ElementNameTruncated,
                ["categoryName"] = item.CategoryName,
                ["categoryNameTruncated"] = item.CategoryNameTruncated,
                ["parameterName"] = item.ParameterName,
                ["parameterNameTruncated"] = item.ParameterNameTruncated,
                ["dataType"] = new Dictionary<string, object?>
                {
                    ["kind"] = item.DataType.Kind.ToString(),
                    ["forgeTypeId"] = item.DataType.ForgeTypeId,
                },
                ["before"] = new Dictionary<string, object?>
                {
                    ["hasValue"] = item.Before.HasValue,
                    ["value"] = item.Before.Value is null ? null : WriteValue(item.Before.Value),
                },
                ["proposed"] = WriteValue(item.Proposed),
            };
        }

        return JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["type"] = "renderReview",
            ["sessionRef"] = model.SessionRef,
            ["createdAt"] = model.CreatedAt,
            ["expiresAt"] = model.ExpiresAt,
            ["items"] = items,
        }, Options);
    }

    public static string SerializeClear() => "{\"type\":\"clearReview\"}";

    public static string SerializeDispatch(ApprovalUiDispatchResult result)
    {
        return JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["type"] = result.Kind == ApprovalUiDispatchKind.Unavailable && !result.ReviewRemains
                ? "unavailable"
                : "actionResult",
            ["outcome"] = result.Kind.ToString(),
            ["reviewRemains"] = result.ReviewRemains,
        }, Options);
    }

    private static Dictionary<string, object?> WriteValue(ApprovalReviewValueModel value)
    {
        return value switch
        {
            ApprovalReviewValueModel.StringValue text => new Dictionary<string, object?>
            {
                ["kind"] = "string",
                ["value"] = text.Value,
            },
            ApprovalReviewValueModel.IntegerValue integer => new Dictionary<string, object?>
            {
                ["kind"] = "integer",
                ["value"] = integer.Value,
            },
            ApprovalReviewValueModel.QuantityValue quantity => new Dictionary<string, object?>
            {
                ["kind"] = "quantity",
                ["value"] = quantity.Value,
                ["unitTypeId"] = quantity.UnitTypeId,
            },
            _ => new Dictionary<string, object?>
            {
                ["kind"] = "unsupported",
            },
        };
    }
}
