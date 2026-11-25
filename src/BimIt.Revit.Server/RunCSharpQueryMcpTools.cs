using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Server;

[McpServerToolType]
internal sealed class RunCSharpQueryMcpTools
{
    private readonly RunCSharpQueryApplicationService _application;

    public RunCSharpQueryMcpTools(RunCSharpQueryApplicationService application)
    {
        _application = application;
    }

    [McpServerTool(
        Name = RunCSharpQueryToolMetadata.Name,
        Title = RunCSharpQueryToolMetadata.Title,
        ReadOnly = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(RunCSharpQueryResult))]
    [Description(RunCSharpQueryToolMetadata.Description)]
    public async Task<CallToolResult> RunAsync(
        McpServer server,
        [Description("Opaque active-document guard from revit_get_context. Required.")] string? document_id,
        [Description("One bounded read-only C# method body. Required.")] string? source,
        [Description("Opaque RevitMCP instance identifier. Omit to auto-select the single eligible instance.")] string? instance_id = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(document_id) || string.IsNullOrEmpty(source))
        {
            return McpCallResultFactory.Error(
                McpToolErrorCodes.InvalidRequest,
                ToolErrorMessages.InvalidRequest);
        }

        var request = new RunCSharpQueryRequest
        {
            DocumentId = document_id,
            Source = source
        };
        var outcome = await _application.ExecuteAsync(instance_id, request, cancellationToken).ConfigureAwait(false);
        return outcome.IsSuccess
            ? McpCallResultFactory.Success(outcome.Result!, server.NegotiatedProtocolVersion)
            : McpCallResultFactory.Error(outcome.ErrorCode!, outcome.ErrorMessage!, outcome.Candidates, outcome.Details);
    }
}
