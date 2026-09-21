using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RevitMCP.Contracts;

namespace RevitMCP.Server;

[McpServerToolType]
internal sealed class DescribeParametersMcpTools
{
    private readonly DescribeParametersApplicationService _application;

    public DescribeParametersMcpTools(DescribeParametersApplicationService application)
    {
        _application = application;
    }

    [McpServerTool(
        Name = DescribeParametersToolMetadata.Name,
        Title = DescribeParametersToolMetadata.Title,
        ReadOnly = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(DescribeParametersResult))]
    [Description(DescribeParametersToolMetadata.Description)]
    public async Task<CallToolResult> DescribeParametersAsync(
        McpServer server,
        [Description("Opaque active-document guard.")] string document_id,
        [Description("Opaque element references whose visible parameters should be discovered, in request order.")] IReadOnlyList<string> element_refs,
        [Description("Opaque RevitMCP instance identifier.")] string? instance_id = null,
        [Description("Parameter source to discover: instance, type, or both.")] string? source = null,
        [Description("Case-insensitive substring filter applied to parameter names.")] string? name_contains = null,
        [Description("Maximum number of unique parameter descriptors to return.")] int limit = DescribeParametersToolMetadata.DefaultLimit,
        CancellationToken cancellationToken = default)
    {
        if (document_id is null || element_refs is null || !TryParseSource(source, out var parsedSource))
        {
            return McpCallResultFactory.Error(
                McpToolErrorCodes.InvalidRequest,
                ToolErrorMessages.InvalidRequest);
        }

        var request = new DescribeParametersRequest
        {
            DocumentId = document_id,
            ElementRefs = element_refs,
            Source = parsedSource,
            NameContains = name_contains,
            Limit = limit
        };

        var outcome = await _application.ExecuteAsync(instance_id, request, cancellationToken).ConfigureAwait(false);
        return outcome.IsSuccess
            ? McpCallResultFactory.Success(outcome.Result!, ResolveProtocolVersion(server))
            : McpCallResultFactory.Error(outcome.ErrorCode!, outcome.ErrorMessage!, outcome.Candidates);
    }

    private static bool TryParseSource(string? source, out DescribeParameterSource parsed)
    {
        if (source is null)
        {
            parsed = DescribeParameterSource.Both;
            return true;
        }

        if (string.Equals(source, "instance", StringComparison.Ordinal))
        {
            parsed = DescribeParameterSource.Instance;
            return true;
        }

        if (string.Equals(source, "type", StringComparison.Ordinal))
        {
            parsed = DescribeParameterSource.Type;
            return true;
        }

        if (string.Equals(source, "both", StringComparison.Ordinal))
        {
            parsed = DescribeParameterSource.Both;
            return true;
        }

        parsed = default;
        return false;
    }

    private static string? ResolveProtocolVersion(McpServer server)
    {
        return server.NegotiatedProtocolVersion;
    }
}
