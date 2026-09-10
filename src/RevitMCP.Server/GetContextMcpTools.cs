using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace RevitMCP.Server;

[McpServerToolType]
internal sealed class GetContextMcpTools
{
    private readonly GetContextApplicationService _application;

    public GetContextMcpTools(GetContextApplicationService application)
    {
        _application = application;
    }

    [McpServerTool(
        Name = GetContextToolMetadata.Name,
        Title = GetContextToolMetadata.Title,
        ReadOnly = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(RevitMCP.Contracts.GetContextResult))]
    [Description(GetContextToolMetadata.Description)]
    public async Task<CallToolResult> GetContextAsync(
        McpServer server,
        [Description("Opaque RevitMCP instance identifier.")] string? instance_id = null,
        CancellationToken cancellationToken = default)
    {
        var outcome = await _application.ExecuteAsync(instance_id, cancellationToken).ConfigureAwait(false);
        return GetContextCallResultFactory.FromOutcome(outcome, ResolveProtocolVersion(server));
    }

    private static string? ResolveProtocolVersion(McpServer server)
    {
        return server.NegotiatedProtocolVersion;
    }
}
