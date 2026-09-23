using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RevitMCP.Contracts;

namespace RevitMCP.Server;

[McpServerToolType]
internal sealed class GetMepTopologyMcpTools
{
    private readonly GetMepTopologyApplicationService _application;

    public GetMepTopologyMcpTools(GetMepTopologyApplicationService application)
    {
        _application = application;
    }

    [McpServerTool(
        Name = GetMepTopologyToolMetadata.Name,
        Title = GetMepTopologyToolMetadata.Title,
        ReadOnly = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(GetMepTopologyResult))]
    [Description(GetMepTopologyToolMetadata.Description)]
    public async Task<CallToolResult> GetMepTopologyAsync(
        McpServer server,
        [Description("Opaque active-document guard.")] string document_id,
        [Description("Opaque seed element references, in request order.")] IReadOnlyList<string> seed_element_refs,
        [Description("Opaque RevitMCP instance identifier.")] string? instance_id = null,
        [Description("Optional physical MEP domain. Omit to include every supported domain.")] MepTopologyDomain? domain = null,
        [Description("Maximum traversal depth. Omit to use the capability default.")] int? max_depth = null,
        [Description("Maximum number of graph nodes. Omit to use the capability default.")] int? max_elements = null,
        [Description("Maximum number of graph edges. Omit to use the capability default.")] int? max_edges = null,
        CancellationToken cancellationToken = default)
    {
        if (document_id is null
            || seed_element_refs is null
            || seed_element_refs.Count is < 1 or > 10
            || seed_element_refs.Any(seed => seed is null)
            || HasDuplicateSeeds(seed_element_refs))
        {
            return McpCallResultFactory.Error(
                McpToolErrorCodes.InvalidRequest,
                ToolErrorMessages.InvalidRequest);
        }

        var request = new GetMepTopologyRequest
        {
            DocumentId = document_id,
            SeedElementRefs = seed_element_refs,
            Domain = domain,
            MaxDepth = max_depth,
            MaxElements = max_elements,
            MaxEdges = max_edges
        };

        var outcome = await _application.ExecuteAsync(instance_id, request, cancellationToken).ConfigureAwait(false);
        return outcome.IsSuccess
            ? McpCallResultFactory.Success(outcome.Result!, ResolveProtocolVersion(server))
            : McpCallResultFactory.Error(outcome.ErrorCode!, outcome.ErrorMessage!, outcome.Candidates);
    }

    private static bool HasDuplicateSeeds(IReadOnlyList<string> seeds)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var seed in seeds)
        {
            if (!seen.Add(seed))
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
