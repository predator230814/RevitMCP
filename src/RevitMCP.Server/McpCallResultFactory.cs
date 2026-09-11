using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace RevitMCP.Server;

internal static class McpCallResultFactory
{
    internal const string StructuredOutputSinceProtocolVersion = "2025-06-18";

    public static CallToolResult Success<T>(T result, string? protocolVersion)
    {
        ArgumentNullException.ThrowIfNull(result);
        var json = JsonSerializer.SerializeToElement(result, McpJson.Options);
        if (SupportsStructuredOutput(protocolVersion))
        {
            return new CallToolResult
            {
                IsError = false,
                StructuredContent = json,
                Content = []
            };
        }

        return new CallToolResult
        {
            IsError = false,
            Content = [new TextContentBlock { Text = json.GetRawText() }]
        };
    }

    public static CallToolResult Error(
        string code,
        string message,
        IReadOnlyList<InstanceCandidate>? candidates = null)
    {
        var payload = candidates is null
            ? JsonSerializer.SerializeToElement(new McpToolError { Code = code, Message = message }, McpJson.Options)
            : JsonSerializer.SerializeToElement(
                new McpToolError { Code = code, Message = message, Candidates = candidates },
                McpJson.Options);

        return new CallToolResult
        {
            IsError = true,
            Content = [new TextContentBlock { Text = payload.GetRawText() }]
        };
    }

    public static bool SupportsStructuredOutput(string? protocolVersion)
    {
        if (string.IsNullOrWhiteSpace(protocolVersion))
        {
            return true;
        }

        return string.CompareOrdinal(protocolVersion, StructuredOutputSinceProtocolVersion) >= 0;
    }

    private sealed class McpToolError
    {
        public required string Code { get; init; }

        public required string Message { get; init; }

        public IReadOnlyList<InstanceCandidate>? Candidates { get; init; }
    }
}
