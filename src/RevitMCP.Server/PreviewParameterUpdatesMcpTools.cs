using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RevitMCP.Contracts;

namespace RevitMCP.Server;

[McpServerToolType]
internal sealed class PreviewParameterUpdatesMcpTools
{
    private readonly PreviewParameterUpdatesApplicationService _application;

    public PreviewParameterUpdatesMcpTools(PreviewParameterUpdatesApplicationService application)
    {
        _application = application;
    }

    [McpServerTool(
        Name = PreviewParameterUpdatesToolMetadata.Name,
        Title = PreviewParameterUpdatesToolMetadata.Title,
        ReadOnly = false,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(PreviewParameterUpdatesResult))]
    [Description(PreviewParameterUpdatesToolMetadata.Description)]
    public async Task<CallToolResult> PreviewParameterUpdatesAsync(
        McpServer server,
        [Description("Opaque active-document guard.")] string document_id,
        [Description("Proposed instance-parameter updates, in request order.")] IReadOnlyList<PreviewParameterUpdate> updates,
        [Description("Opaque RevitMCP instance identifier.")] string? instance_id = null,
        CancellationToken cancellationToken = default)
    {
        if (document_id is null
            || updates is null
            || updates.Count is < 1 or > 20
            || updates.Any(update => update is null
                || update.ElementRef is null
                || update.ParameterRef is null
                || update.Value is null)
            || HasRejectedProposedValue(updates)
            || HasDuplicatePairs(updates))
        {
            return McpCallResultFactory.Error(
                McpToolErrorCodes.InvalidRequest,
                ToolErrorMessages.InvalidRequest);
        }

        var request = new PreviewParameterUpdatesRequest
        {
            DocumentId = document_id,
            Updates = updates
        };

        var outcome = await _application.ExecuteAsync(instance_id, request, cancellationToken).ConfigureAwait(false);
        return outcome.IsSuccess
            ? McpCallResultFactory.Success(outcome.Result!, ResolveProtocolVersion(server))
            : McpCallResultFactory.Error(outcome.ErrorCode!, outcome.ErrorMessage!, outcome.Candidates);
    }

    private static bool HasRejectedProposedValue(IReadOnlyList<PreviewParameterUpdate> updates)
    {
        foreach (var update in updates)
        {
            switch (update.Value)
            {
                case PreviewParameterStringValue text when text.Value is null || text.Value.Length > 512:
                case PreviewParameterQuantityValue quantity when string.IsNullOrEmpty(quantity.UnitTypeId):
                    return true;
            }
        }

        return false;
    }

    private static bool HasDuplicatePairs(IReadOnlyList<PreviewParameterUpdate> updates)
    {
        var seen = new HashSet<(string ElementRef, string ParameterRef)>(PairComparer.Ordinal);
        foreach (var update in updates)
        {
            if (!seen.Add((update.ElementRef, update.ParameterRef)))
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

    private sealed class PairComparer : IEqualityComparer<(string ElementRef, string ParameterRef)>
    {
        public static PairComparer Ordinal { get; } = new();

        public bool Equals(
            (string ElementRef, string ParameterRef) x,
            (string ElementRef, string ParameterRef) y)
        {
            return string.Equals(x.ElementRef, y.ElementRef, StringComparison.Ordinal)
                && string.Equals(x.ParameterRef, y.ParameterRef, StringComparison.Ordinal);
        }

        public int GetHashCode((string ElementRef, string ParameterRef) obj)
        {
            return HashCode.Combine(
                StringComparer.Ordinal.GetHashCode(obj.ElementRef),
                StringComparer.Ordinal.GetHashCode(obj.ParameterRef));
        }
    }
}
