namespace BimIt.Revit.Assistant;

internal sealed class UnavailableAssistantAgentRuntime : IAssistantAgentRuntime
{
    private readonly string _message;

    public UnavailableAssistantAgentRuntime(string? message = null)
    {
        _message = string.IsNullOrWhiteSpace(message)
            ? "The AI provider is unavailable."
            : message;
        Status = new AssistantProviderStatus("GitHub Copilot", AssistantProviderState.Unavailable, null, _message);
    }

    public AssistantProviderStatus Status { get; private set; }

    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        Status = new AssistantProviderStatus("GitHub Copilot", AssistantProviderState.Unavailable, null, _message);
        return Task.CompletedTask;
    }

    public Task<AssistantAgentTurn> SendAsync(AssistantAgentRequest request, CancellationToken cancellationToken)
    {
        return Task.FromResult(new AssistantAgentTurn("BIM-IT Assistant", _message, true));
    }

    public Task ResetConversationAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
