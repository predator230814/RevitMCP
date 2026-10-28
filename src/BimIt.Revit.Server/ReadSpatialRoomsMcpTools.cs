using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Server;

[McpServerToolType]
internal sealed class ReadSpatialRoomsMcpTools
{
    private readonly ReadSpatialRoomsApplicationService _application;

    public ReadSpatialRoomsMcpTools(ReadSpatialRoomsApplicationService application)
    {
        _application = application;
    }

    [McpServerTool(
        Name = ReadSpatialRoomsToolMetadata.Name,
        Title = ReadSpatialRoomsToolMetadata.Title,
        ReadOnly = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ReadSpatialRoomsResult))]
    [Description(ReadSpatialRoomsToolMetadata.Description)]
    public async Task<CallToolResult> ReadSpatialRoomsAsync(
        McpServer server,
        [Description("Opaque active-document guard. Required.")] string? document_id,
        [Description("Optional room-name word, such as studio. Omit to read every bounded room.")] string? name_contains = null,
        [Description("Maximum rooms to return, from 1 to 100.")] int? limit = null,
        [Description("Opaque RevitMCP instance identifier. Omit to auto-select the single eligible instance.")] string? instance_id = null,
        CancellationToken cancellationToken = default)
    {
        var outcome = await _application.ExecuteAsync(
            instance_id,
            new ReadSpatialRoomsRequest { DocumentId = document_id, NameContains = name_contains, Limit = limit },
            cancellationToken).ConfigureAwait(false);
        return outcome.IsSuccess
            ? McpCallResultFactory.Success(outcome.Result!, server.NegotiatedProtocolVersion)
            : McpCallResultFactory.Error(outcome.ErrorCode!, outcome.ErrorMessage!, outcome.Candidates);
    }
}
