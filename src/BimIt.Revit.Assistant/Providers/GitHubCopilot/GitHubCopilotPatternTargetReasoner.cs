using BimIt.Revit.Assistant.Patterns;
using GitHub.Copilot;

namespace BimIt.Revit.Assistant.Providers.GitHubCopilot;

internal sealed class GitHubCopilotPatternTargetReasoner : IPatternTargetReasoner
{
    private readonly CopilotClient _client;
    private readonly string _directory;
    private readonly string? _model;

    public GitHubCopilotPatternTargetReasoner(CopilotClient client, string directory, string? model)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _directory = directory ?? throw new ArgumentNullException(nameof(directory));
        _model = model;
    }

    public async Task<PatternTargetReasoningResult> ReasonAsync(PatternTargetReasoningRequest request, CancellationToken cancellationToken)
    {
        CopilotSession? session = null;
        try
        {
            string? content;
            try
            {
                session = await _client.CreateSessionAsync(
                    GitHubCopilotSessionBlueprint.CreateTargetSessionConfig(_directory, _model),
                    cancellationToken).ConfigureAwait(false);
                var message = await session.SendAndWaitAsync(
                    new MessageOptions { Prompt = PatternTargetContext.Compose(request) },
                    AssistantProviderTimeouts.Default.Turn,
                    cancellationToken).ConfigureAwait(false);
                content = message?.Data?.Content;
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                return PatternTargetReasoningResult.Failed(PatternTargetReasoningResult.ProviderError);
            }

            return PatternTargetResponseParser.TryParse(content, out var result)
                ? result
                : PatternTargetReasoningResult.Failed(PatternTargetReasoningResult.UnreadableResponse);
        }
        finally
        {
            if (session is not null)
            {
                try
                {
                    await session.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception)
                {
                }
            }
        }
    }
}
