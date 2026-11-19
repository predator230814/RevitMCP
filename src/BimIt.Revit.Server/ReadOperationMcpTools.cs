using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Server;

[McpServerToolType]
internal sealed class ReadOperationMcpTools
{
    private readonly ReadOperationApplicationService _application;

    public ReadOperationMcpTools(ReadOperationApplicationService application)
    {
        _application = application;
    }

    [McpServerTool(
        Name = ReadOperationToolMetadata.Name,
        Title = ReadOperationToolMetadata.Title,
        ReadOnly = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ReadOperationResult))]
    [Description(ReadOperationToolMetadata.Description)]
    public async Task<CallToolResult> ReadAsync(
        McpServer server,
        [Description("Opaque active-document guard from revit_get_context. Required.")] string? document_id,
        [Description("Registered operation id from revit_lookup. Required. There is no default.")] string? operation,
        [Description("Contract version from revit_lookup. Required. There is no default version.")] int? contract_version,
        [Description("Operation inputs. Must match the schema returned by revit_lookup.")] JsonElement inputs,
        [Description("Page size for a cursor-paginated operation, from 1 to 200. Rejected when pagination is none.")] int? page_size = null,
        [Description("Opaque next_cursor from the previous page. Rejected when pagination is none.")] string? cursor = null,
        [Description("Opaque RevitMCP instance identifier. Omit to auto-select the single eligible instance.")] string? instance_id = null,
        CancellationToken cancellationToken = default)
    {
        if (document_id is null || operation is null || contract_version is null || inputs.ValueKind != JsonValueKind.Object)
        {
            return McpCallResultFactory.Error(
                McpToolErrorCodes.InvalidRequest,
                ToolErrorMessages.InvalidRequest);
        }

        var request = new ReadOperationRequest
        {
            DocumentId = document_id,
            Operation = operation,
            ContractVersion = contract_version,
            Inputs = inputs.Clone(),
            PageSize = page_size,
            Cursor = cursor
        };
        var outcome = await _application.ExecuteAsync(instance_id, request, cancellationToken).ConfigureAwait(false);
        return outcome.IsSuccess
            ? McpCallResultFactory.Success(outcome.Result!, server.NegotiatedProtocolVersion)
            : McpCallResultFactory.Error(outcome.ErrorCode!, outcome.ErrorMessage!, outcome.Candidates);
    }
}
