using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace BimIt.Revit.Assistant;

internal sealed class McpAssistantToolSession : IAssistantToolSession, IAsyncDisposable
{
    private readonly string _serverExecutable;
    private McpClient? _client;

    public McpAssistantToolSession(string serverExecutable)
    {
        if (string.IsNullOrWhiteSpace(serverExecutable))
        {
            throw new ArgumentException("The MCP server executable is required.", nameof(serverExecutable));
        }

        _serverExecutable = serverExecutable;
    }

    public async Task<AssistantMcpReadiness> InitializeAsync(CancellationToken cancellationToken)
    {
        try
        {
            _client = await McpClient.CreateAsync(
                new StdioClientTransport(new StdioClientTransportOptions
                {
                    Name = "BimIt.Revit.Assistant",
                    Command = _serverExecutable,
                    Arguments = [],
                    ShutdownTimeout = TimeSpan.FromSeconds(5),
                }),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            var tools = await _client.ListToolsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            var names = tools.Select(tool => tool.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray();
            return new AssistantMcpReadiness(true, names.Length, names, null);
        }
        catch (Exception exception)
        {
            return new AssistantMcpReadiness(false, 0, [], exception.Message);
        }
    }

    public async Task<AssistantToolCall> CallAsync(
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        if (_client is null)
        {
            return new AssistantToolCall(true, "mcp_unavailable", "The MCP client is not initialized.", null);
        }

        if (AssistantTools.IsTrustedWriteTool(toolName) || !AssistantTools.AllowedReadTools.Contains(toolName, StringComparer.Ordinal))
        {
            return new AssistantToolCall(true, "tool_not_allowed", "This Assistant workflow cannot call that tool.", null);
        }

        var result = await _client.CallToolAsync(
            toolName,
            arguments.ToDictionary(pair => pair.Key, pair => pair.Value),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (result.IsError != true)
        {
            return new AssistantToolCall(false, null, null, result.StructuredContent);
        }

        var text = result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text;
        TryReadError(text, out var code, out var message);
        return new AssistantToolCall(true, code, message ?? text, null);
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is not null)
        {
            await _client.DisposeAsync().ConfigureAwait(false);
            _client = null;
        }
    }

    private static void TryReadError(string? json, out string? code, out string? message)
    {
        code = null;
        message = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("code", out var codeProperty))
            {
                code = codeProperty.GetString();
            }

            if (document.RootElement.TryGetProperty("message", out var messageProperty))
            {
                message = messageProperty.GetString();
            }
        }
        catch (JsonException)
        {
            message = json;
        }
    }
}
