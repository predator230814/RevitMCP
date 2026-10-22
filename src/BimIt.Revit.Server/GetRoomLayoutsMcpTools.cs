using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Server;

[McpServerToolType]
internal sealed class GetRoomLayoutsMcpTools
{
    private readonly GetRoomLayoutsApplicationService _application;

    public GetRoomLayoutsMcpTools(GetRoomLayoutsApplicationService application)
    {
        _application = application;
    }

    [McpServerTool(
        Name = GetRoomLayoutsToolMetadata.Name,
        Title = GetRoomLayoutsToolMetadata.Title,
        ReadOnly = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(GetRoomLayoutsResult))]
    [Description(GetRoomLayoutsToolMetadata.Description)]
    public async Task<CallToolResult> GetRoomLayoutsAsync(
        McpServer server,
        [Description("Opaque active-document guard. Required.")] string? document_id,
        [Description("One to twenty opaque Room element references.")] string[]? element_refs,
        [Description("Opaque RevitMCP instance identifier. Omit to auto-select the single eligible instance.")] string? instance_id = null,
        CancellationToken cancellationToken = default)
    {
        var request = new GetRoomLayoutsRequest
        {
            DocumentId = document_id,
            ElementRefs = element_refs
        };

        var outcome = await _application.ExecuteAsync(instance_id, request, cancellationToken).ConfigureAwait(false);
        return outcome.IsSuccess
            ? McpCallResultFactory.Success(outcome.Result!, server.NegotiatedProtocolVersion)
            : McpCallResultFactory.Error(outcome.ErrorCode!, outcome.ErrorMessage!, outcome.Candidates);
    }
}
