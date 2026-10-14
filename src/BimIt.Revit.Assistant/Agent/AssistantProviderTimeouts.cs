namespace BimIt.Revit.Assistant;

internal sealed class AssistantProviderTimeouts
{
    public const string InitializationTimedOut = "The AI provider did not become ready in time.";

    public const string ResetTimedOut = "The AI provider did not reset in time.";

    public const string TurnTimedOut = "The AI provider did not answer in time.";

    public static AssistantProviderTimeouts Default { get; } = new(
        TimeSpan.FromSeconds(30),
        TimeSpan.FromSeconds(15),
        TimeSpan.FromSeconds(90));

    public AssistantProviderTimeouts(TimeSpan initialization, TimeSpan reset, TimeSpan turn)
    {
        if (initialization <= TimeSpan.Zero || reset <= TimeSpan.Zero || turn <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(initialization), "Provider deadlines must be positive.");
        }

        Initialization = initialization;
        Reset = reset;
        Turn = turn;
    }

    public TimeSpan Initialization { get; }

    public TimeSpan Reset { get; }

    public TimeSpan Turn { get; }

    public TimeSpan CompletionGrace { get; } = TimeSpan.FromSeconds(2);
}
