namespace BimIt.Revit.Addin.Assistant;

internal sealed record AssistantTranscriptEntry(string Role, string Title, string Body);

internal sealed class AssistantConversation
{
    private readonly List<AssistantTranscriptEntry> _entries = [];

    public AssistantConversation()
    {
        ConversationId = NewId();
    }

    public string ConversationId { get; private set; }

    public IReadOnlyList<AssistantTranscriptEntry> Entries => _entries;

    public void Add(string role, string title, string body)
    {
        _entries.Add(new AssistantTranscriptEntry(role, title, body));
    }

    public void Reset()
    {
        _entries.Clear();
        ConversationId = NewId();
    }

    private static string NewId() => Guid.NewGuid().ToString("N");
}
