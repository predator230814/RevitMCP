using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RevitMCP.Contracts;

namespace RevitMCP.Server;

[McpServerToolType]
internal sealed class GetWarningsMcpTools
{
    private readonly GetWarningsApplicationService _application;

    public GetWarningsMcpTools(GetWarningsApplicationService application)
    {
        _application = application;
    }

    [McpServerTool(
        Name = GetWarningsToolMetadata.Name,
        Title = GetWarningsToolMetadata.Title,
        ReadOnly = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(GetWarningsResult))]
    [Description(GetWarningsToolMetadata.Description)]
    public async Task<CallToolResult> GetWarningsAsync(
        McpServer server,
        [Description("Opaque active-document guard.")] string document_id,
        [Description("Opaque RevitMCP instance identifier.")] string? instance_id = null,
        [Description("Optional severity filter. Omit to include every mapped severity.")] WarningSeverity? severity = null,
        [Description("Optional exact failure key. Omit to include every key. An empty string matches only empty keys.")] string? failure_key = null,
        [Description("Optional opaque element references. Omit to include every persistent failure.")] IReadOnlyList<string>? element_refs = null,
        [Description("Maximum number of warning messages. Omit to use the capability default.")] int? max_warnings = null,
        [Description("Maximum number of element references per warning. Omit to use the capability default.")] int? max_elements_per_warning = null,
        CancellationToken cancellationToken = default)
    {
        if (document_id is null
            || (element_refs is not null
                && (element_refs.Count is < 1 or > 10
                    || element_refs.Any(elementRef => elementRef is null)
                    || HasDuplicateRefs(element_refs))))
        {
            return McpCallResultFactory.Error(
                McpToolErrorCodes.InvalidRequest,
                ToolErrorMessages.InvalidRequest);
        }

        var request = new GetWarningsRequest
        {
            DocumentId = document_id,
            Severity = severity,
            FailureKey = failure_key,
            ElementRefs = element_refs,
            MaxWarnings = max_warnings,
            MaxElementsPerWarning = max_elements_per_warning
        };

        var outcome = await _application.ExecuteAsync(instance_id, request, cancellationToken).ConfigureAwait(false);
        return outcome.IsSuccess
            ? McpCallResultFactory.Success(outcome.Result!, ResolveProtocolVersion(server))
            : McpCallResultFactory.Error(outcome.ErrorCode!, outcome.ErrorMessage!, outcome.Candidates);
    }

    private static bool HasDuplicateRefs(IReadOnlyList<string> elementRefs)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var elementRef in elementRefs)
        {
            if (!seen.Add(elementRef))
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
