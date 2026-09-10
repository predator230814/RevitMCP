using ModelContextProtocol.Protocol;
using RevitMCP.Contracts;

namespace RevitMCP.Server;

internal static class GetContextCallResultFactory
{
    internal const string StructuredOutputSinceProtocolVersion =
        McpCallResultFactory.StructuredOutputSinceProtocolVersion;

    public static CallToolResult FromOutcome(GetContextOutcome outcome, string? protocolVersion)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        return outcome.IsSuccess
            ? Success(outcome.Result!, protocolVersion)
            : Error(outcome.ErrorCode!, outcome.ErrorMessage!, outcome.Candidates);
    }

    public static CallToolResult Success(GetContextResult result, string? protocolVersion)
    {
        return McpCallResultFactory.Success(result, protocolVersion);
    }

    public static CallToolResult Error(
        string code,
        string message,
        IReadOnlyList<InstanceCandidate>? candidates = null)
    {
        return McpCallResultFactory.Error(code, message, candidates);
    }

    public static bool SupportsStructuredOutput(string? protocolVersion)
    {
        return McpCallResultFactory.SupportsStructuredOutput(protocolVersion);
    }
}
