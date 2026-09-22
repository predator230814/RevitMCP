using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RevitMCP.Contracts;

namespace RevitMCP.Server;

[McpServerToolType]
internal sealed class GetParameterValuesMcpTools
{
    private readonly GetParameterValuesApplicationService _application;

    public GetParameterValuesMcpTools(GetParameterValuesApplicationService application)
    {
        _application = application;
    }

    [McpServerTool(
        Name = GetParameterValuesToolMetadata.Name,
        Title = GetParameterValuesToolMetadata.Title,
        ReadOnly = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(GetParameterValuesResult))]
    [Description(GetParameterValuesToolMetadata.Description)]
    public async Task<CallToolResult> GetParameterValuesAsync(
        McpServer server,
        [Description("Opaque active-document guard.")] string document_id,
        [Description("Explicit element and parameter reference pairs to read, in request order.")] IReadOnlyList<GetParameterValueRead> reads,
        [Description("Opaque RevitMCP instance identifier.")] string? instance_id = null,
        CancellationToken cancellationToken = default)
    {
        if (document_id is null
            || reads is null
            || reads.Any(read => read is null || read.ElementRef is null || read.ParameterRef is null)
            || HasDuplicatePairs(reads))
        {
            return McpCallResultFactory.Error(
                McpToolErrorCodes.InvalidRequest,
                ToolErrorMessages.InvalidRequest);
        }

        var request = new GetParameterValuesRequest
        {
            DocumentId = document_id,
            Reads = reads
        };

        var outcome = await _application.ExecuteAsync(instance_id, request, cancellationToken).ConfigureAwait(false);
        return outcome.IsSuccess
            ? McpCallResultFactory.Success(outcome.Result!, ResolveProtocolVersion(server))
            : McpCallResultFactory.Error(outcome.ErrorCode!, outcome.ErrorMessage!, outcome.Candidates);
    }

    private static bool HasDuplicatePairs(IReadOnlyList<GetParameterValueRead> reads)
    {
        var seen = new HashSet<(string ElementRef, string ParameterRef)>();
        foreach (var read in reads)
        {
            if (!seen.Add((read.ElementRef, read.ParameterRef)))
            {
                return true;
            }
        }

        return false;
    }

    private static string? ResolveProtocolVersion(McpServer server)
    {
        return server.NegotiatedProtocolVersion;
    }
}
