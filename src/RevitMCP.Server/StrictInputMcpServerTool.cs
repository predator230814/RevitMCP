using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace RevitMCP.Server;

internal sealed class StrictInputMcpServerTool : DelegatingMcpServerTool
{
    private StrictInputMcpServerTool(McpServerTool innerTool)
        : base(innerTool)
    {
    }

    public static McpServerTool Wrap(McpServerTool innerTool)
    {
        ArgumentNullException.ThrowIfNull(innerTool);
        return new StrictInputMcpServerTool(innerTool);
    }

    public override async ValueTask<CallToolResult> InvokeAsync(
        RequestContext<CallToolRequestParams> request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (ClosedSchemaArgumentValidator.HasUnexpectedProperties(
                request.Params?.Arguments,
                ProtocolTool.InputSchema))
        {
            return InvalidRequest();
        }

        try
        {
            return await base.InvokeAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (ContainsJsonException(exception))
        {
            return InvalidRequest();
        }
    }

    private static CallToolResult InvalidRequest()
    {
        return McpCallResultFactory.Error(
            McpToolErrorCodes.InvalidRequest,
            ToolErrorMessages.InvalidRequest);
    }

    private static bool ContainsJsonException(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is JsonException)
            {
                return true;
            }
        }

        return false;
    }
}
