namespace BimIt.Revit.Assistant;

/// <summary>
/// One observable Assistant action. The label is BIM-IT text. <see cref="Tool"/> is a trusted name when the
/// action is a real tool call. Arguments, model values, and private reasoning never belong here.
/// </summary>
public sealed record AssistantActivity(string Kind, string State, string Label, string? Tool = null);

public static class AssistantActivityKind
{
    public const string Preparing = "preparing";

    public const string ReadingSkill = "reading_skill";

    public const string McpRead = "mcp_read";

    public const string PreparingResponse = "preparing_response";
}

public static class AssistantActivityState
{
    public const string Started = "started";

    public const string Completed = "completed";
}

/// <summary>Receives activities for the chat turn that is in progress. A provider must not retain the sink.</summary>
public interface IAssistantActivitySink
{
    void Report(AssistantActivity activity);
}

/// <summary>
/// Turn-scoped activity bus. Reports reach only the sink bound for the current chat request.
/// </summary>
internal sealed class AssistantActivityBus
{
    public const int MaxPerTurn = 32;

    private readonly object _gate = new();
    private IAssistantActivitySink? _sink;
    private int _count;

    public void Bind(IAssistantActivitySink? sink)
    {
        lock (_gate)
        {
            _sink = sink;
            _count = 0;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _sink = null;
        }
    }

    public void Report(AssistantActivity? activity)
    {
        if (activity is null)
        {
            return;
        }

        lock (_gate)
        {
            if (_sink is null || _count >= MaxPerTurn)
            {
                return;
            }

            _count++;
            _sink.Report(activity);
        }
    }
}

/// <summary>
/// Safe labels for real Assistant work. Tool descriptions are not copied: they often name arguments.
/// The visible phrase comes from the trusted tool name.
/// </summary>
internal static class AssistantActivityLabels
{
    public static AssistantActivity Preparing()
        => new(AssistantActivityKind.Preparing, AssistantActivityState.Started, "Preparing");

    public static AssistantActivity PreparingResponse()
        => new(AssistantActivityKind.PreparingResponse, AssistantActivityState.Started, "Preparing the final response");

    public static AssistantActivity ReadingSkill(string state)
        => new(
            AssistantActivityKind.ReadingSkill,
            state,
            state == AssistantActivityState.Completed ? "Read a Skill" : "Reading a Skill",
            "bimit_read_skill");

    public static AssistantActivity McpRead(string toolName, string? description, string state)
        => new(AssistantActivityKind.McpRead, state, ForTool(toolName, description, state == AssistantActivityState.Completed), toolName);

    public static string ForTool(string toolName, string? description, bool completed)
    {
        _ = description;
        var present = toolName switch
        {
            "revit_get_context" => "Reading model context",
            "revit_lookup" => "Discovering Revit capabilities",
            "revit_read" => "Reading Revit data",
            _ => FromName(toolName),
        };
        return completed ? Completed(present) : present;
    }

    private static string FromName(string toolName)
    {
        if (!toolName.StartsWith("revit_", StringComparison.Ordinal) || toolName.Length <= "revit_".Length)
        {
            return "Reading Revit data";
        }

        var parts = toolName["revit_".Length..].Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return "Reading Revit data";
        }

        var subject = string.Join(' ', parts.Skip(1).Select(Word));
        if (string.IsNullOrEmpty(subject))
        {
            subject = "Revit data";
        }

        return parts[0] switch
        {
            "get" or "read" => "Reading " + subject,
            "query" => "Querying " + subject,
            "describe" => "Describing " + subject,
            "list" => "Listing " + subject,
            _ => "Reading " + string.Join(' ', parts.Select(Word)),
        };
    }

    private static string Word(string token)
    {
        return token switch
        {
            "mep" => "MEP",
            "api" => "API",
            "hvac" => "HVAC",
            _ => token,
        };
    }

    private static string Completed(string present)
    {
        if (present.StartsWith("Reading ", StringComparison.Ordinal))
        {
            return "Read " + present["Reading ".Length..];
        }

        if (present.StartsWith("Discovering ", StringComparison.Ordinal))
        {
            return "Discovered " + present["Discovering ".Length..];
        }

        if (present.StartsWith("Querying ", StringComparison.Ordinal))
        {
            return "Queried " + present["Querying ".Length..];
        }

        if (present.StartsWith("Describing ", StringComparison.Ordinal))
        {
            return "Described " + present["Describing ".Length..];
        }

        if (present.StartsWith("Listing ", StringComparison.Ordinal))
        {
            return "Listed " + present["Listing ".Length..];
        }

        return present;
    }
}
