namespace BimIt.Revit.Assistant;

public enum AssistantProviderState
{
    NotConfigured,
    Starting,
    Ready,
    Unavailable,
}

public sealed record AssistantProviderStatus(string Name, AssistantProviderState State, string? Model, string? SafeError)
{
    public const string NotConfiguredLabel = "Not configured";

    public static AssistantProviderStatus NotConfigured { get; } = new("", AssistantProviderState.NotConfigured, null, null);

    public string StateToken => State switch
    {
        AssistantProviderState.Starting => "starting",
        AssistantProviderState.Ready => "ready",
        AssistantProviderState.Unavailable => "unavailable",
        _ => "not_configured",
    };

    public string Label
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Name) || State == AssistantProviderState.NotConfigured)
            {
                return NotConfiguredLabel;
            }

            return State switch
            {
                AssistantProviderState.Starting => Name + " · starting",
                AssistantProviderState.Unavailable => Name + " · unavailable",
                AssistantProviderState.Ready when IsConcreteModel(Model) => Name + " · " + Model,
                AssistantProviderState.Ready => Name + " · auto",
                _ => Name + " · unavailable",
            };
        }
    }

    public static bool IsConcreteModel(string? model)
    {
        return !string.IsNullOrWhiteSpace(model)
            && !string.Equals(model, "auto", StringComparison.Ordinal)
            && IsModelToken(model);
    }

    public static bool IsModelToken(string? model)
    {
        return !string.IsNullOrWhiteSpace(model)
            && model.Length <= 80
            && model.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or ':' or '-');
    }
}
