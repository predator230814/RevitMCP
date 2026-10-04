using System.Text.Json;
using System.Text.Json.Serialization;

namespace RevitMCP.WebView2Spike.Messaging;

public static class SpikeContent
{
    public const string LocalHostName = "revitmcp-spike.local";

    public const string LocalOrigin = "https://revitmcp-spike.local/";
}

public static class SpikeJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

public readonly record struct SpikeUiMessageResult(bool Accepted, string? MessageType, string Rejection)
{
    public static SpikeUiMessageResult Accept(string messageType) => new(true, messageType, "");

    public static SpikeUiMessageResult Reject(string? messageType, string rejection) => new(false, messageType, rejection);
}

/// <summary>
/// Parses JavaScript messages. The only accepted inbound type is <c>ping</c>.
/// </summary>
public static class SpikeMessageParser
{
    public static SpikeUiMessageResult Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return SpikeUiMessageResult.Reject(null, "empty");
        }

        try
        {
            var message = JsonSerializer.Deserialize<SpikeUiMessage>(json, SpikeJson.Options);
            if (message is null || string.IsNullOrWhiteSpace(message.Type))
            {
                return SpikeUiMessageResult.Reject(null, "missing-type");
            }

            if (message.Type == "ping")
            {
                return SpikeUiMessageResult.Accept("ping");
            }

            return SpikeUiMessageResult.Reject(message.Type, "unknown");
        }
        catch (JsonException)
        {
            return SpikeUiMessageResult.Reject(null, "malformed");
        }
    }
}

public sealed class SpikeUiMessage
{
    public string? Type { get; set; }
}

public sealed class WebView2AssemblyInfo
{
    public string Name { get; init; } = "";

    public string Version { get; init; } = "";

    public string Location { get; init; } = "";
}

public sealed class HostReadyMessage
{
    public string Type { get; init; } = "hostReady";

    public string RevitVersion { get; init; } = "";

    public string RevitBuild { get; init; } = "";

    public string DotnetRuntime { get; init; } = "";

    public string WebView2SdkVersion { get; init; } = "";

    public string WebView2WpfVersion { get; init; } = "";

    public string WebView2RuntimeVersion { get; init; } = "";

    public bool CoreWebView2Initialized { get; init; }

    public string ContentOrigin { get; init; } = SpikeContent.LocalOrigin;

    public string LastHostToUi { get; init; } = "";

    public string LastUiToHost { get; init; } = "";

    public IReadOnlyList<WebView2AssemblyInfo> Assemblies { get; init; } = [];
}

public sealed class PongMessage
{
    public string Type { get; init; } = "pong";

    public string LastHostToUi { get; init; } = "";

    public string LastUiToHost { get; init; } = "";
}

public sealed class RejectedMessage
{
    public string Type { get; init; } = "rejected";

    public string Reason { get; init; } = "";
}

public static class SpikeOutbound
{
    public static string HostReady(HostReadyMessage message) => JsonSerializer.Serialize(message, SpikeJson.Options);

    public static string Pong(string lastHostToUi, string lastUiToHost) =>
        JsonSerializer.Serialize(new PongMessage { LastHostToUi = lastHostToUi, LastUiToHost = lastUiToHost }, SpikeJson.Options);

    public static string Rejected(string reason) =>
        JsonSerializer.Serialize(new RejectedMessage { Reason = reason }, SpikeJson.Options);
}
