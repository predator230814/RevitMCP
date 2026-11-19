using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Server;

[McpServerToolType]
internal sealed class LookupReadOperationsMcpTools
{
    private readonly LookupReadOperationsApplicationService _application;

    public LookupReadOperationsMcpTools(LookupReadOperationsApplicationService application)
    {
        _application = application;
    }

    [McpServerTool(
        Name = LookupReadOperationsToolMetadata.Name,
        Title = LookupReadOperationsToolMetadata.Title,
        ReadOnly = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(LookupReadOperationsResult))]
    [Description(LookupReadOperationsToolMetadata.Description)]
    public async Task<CallToolResult> LookupAsync(
        McpServer server,
        [Description("Registered operation id. Omit to search. Present to return the full descriptor.")] string? operation = null,
        [Description("Contract version. Optional in exact mode. Illegal in search mode.")] int? contract_version = null,
        [Description("Case-insensitive substring of the operation id, title, or description. Search mode only.")] string? query = null,
        [Description("Search page start, zero or greater. Search mode only.")] int? offset = null,
        [Description("Search page size, from 1 to 50. Search mode only.")] int? limit = null,
        [Description("Opaque RevitMCP instance identifier. Omit to auto-select the single eligible instance.")] string? instance_id = null,
        CancellationToken cancellationToken = default)
    {
        var request = new LookupReadOperationsRequest
        {
            Operation = operation,
            ContractVersion = contract_version,
            Query = query,
            Offset = offset,
            Limit = limit
        };
        var outcome = await _application.ExecuteAsync(instance_id, request, cancellationToken).ConfigureAwait(false);
        return outcome.IsSuccess
            ? McpCallResultFactory.Success(outcome.Result!, server.NegotiatedProtocolVersion)
            : McpCallResultFactory.Error(outcome.ErrorCode!, outcome.ErrorMessage!, outcome.Candidates);
    }
}
