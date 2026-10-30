namespace BimIt.Revit.Assistant;

public sealed record AssistantAgentTurn(
    string Title,
    string Body,
    bool IsError,
    IReadOnlyList<string>? UsedTools = null,
    string? PatternReview = null,
    IReadOnlyList<AssistantActivity>? Activities = null);

/// <param name="Skills">Enabled, ready Skill summaries reloaded for this turn. Metadata only, never Skill content.</param>
public sealed record AssistantAgentRequest(
    string Text,
    AssistantDocumentArtifact? Attachment = null,
    IReadOnlyList<BimIt.Revit.Skills.SkillSummary>? Skills = null);

public interface IAssistantAgentRuntime : IAsyncDisposable
{
    AssistantProviderStatus Status { get; }

    Task InitializeAsync(CancellationToken cancellationToken);

    Task<AssistantAgentTurn> SendAsync(AssistantAgentRequest request, CancellationToken cancellationToken);

    Task ResetConversationAsync(CancellationToken cancellationToken);
}
