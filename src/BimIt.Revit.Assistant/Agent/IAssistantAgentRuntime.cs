namespace BimIt.Revit.Assistant;

public sealed record AssistantAgentTurn(string Title, string Body, bool IsError, IReadOnlyList<string>? UsedTools = null);

public sealed record AssistantAgentRequest(string Text, AssistantDocumentArtifact? Attachment = null);

public interface IAssistantAgentRuntime : IAsyncDisposable
{
    AssistantProviderStatus Status { get; }

    Task InitializeAsync(CancellationToken cancellationToken);

    Task<AssistantAgentTurn> SendAsync(AssistantAgentRequest request, CancellationToken cancellationToken);

    Task ResetConversationAsync(CancellationToken cancellationToken);
}
