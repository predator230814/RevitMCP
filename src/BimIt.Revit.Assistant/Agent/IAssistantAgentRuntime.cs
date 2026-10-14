namespace BimIt.Revit.Assistant;

public sealed record AssistantAgentTurn(string Title, string Body, bool IsError, IReadOnlyList<string>? UsedTools = null);

public interface IAssistantAgentRuntime : IAsyncDisposable
{
    AssistantProviderStatus Status { get; }

    Task InitializeAsync(CancellationToken cancellationToken);

    Task<AssistantAgentTurn> SendAsync(string userText, CancellationToken cancellationToken);

    Task ResetConversationAsync(CancellationToken cancellationToken);
}
