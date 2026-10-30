using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace BimIt.Revit.Assistant.Ipc;

/// <summary>
/// Settings → Skills over the Assistant pipe. The WebView never sends a path. Only the add-in adds one, from a native
/// folder picker, for <c>addSource</c> and <c>import</c>. The view sent back carries Skill names, descriptions, source
/// badges, hashes, status, and SKILL.md text, and never a filesystem path.
/// </summary>
public static partial class AssistantSkillsIpc
{
    public const int MaxKeyLength = 160;

    public const int MaxViewCharacters = 230_000;

    public const int MaxSkills = 200;

    public const int MaxSources = 12;

    public const int MaxDetailCharacters = 131_072;

    public const int MaxDescriptionCharacters = 1_024;

    public const int MaxDiagnosticCharacters = 400;

    public static readonly string[] Actions =
        ["list", "reload", "detail", "enable", "disable", "openCustomFolder", "addSource", "removeSource", "import"];

    public static readonly string[] Statuses = ["ready", "disabled", "invalid", "conflict", "unavailable"];

    public static readonly string[] Sources = ["built_in", "custom", "external"];

    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string SerializeRequest(Request request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return JsonSerializer.Serialize(request, Options);
    }

    public static bool TryParseRequest(string? json, out Request request)
    {
        request = new Request();
        if (string.IsNullOrWhiteSpace(json) || json.Length > 2_000)
        {
            return false;
        }

        try
        {
            var body = JsonSerializer.Deserialize<Request>(json, Options);
            if (body is null || body.Action is null || !Actions.Contains(body.Action, StringComparer.Ordinal))
            {
                return false;
            }

            var needsKey = body.Action is "detail" or "enable" or "disable";
            var needsSource = body.Action is "removeSource";
            var needsPath = body.Action is "addSource" or "import";
            if (needsKey != (body.Key is not null)
                || needsSource != (body.SourceId is not null)
                || needsPath != (body.Path is not null))
            {
                return false;
            }

            if (body.Key is not null && !IsKey(body.Key))
            {
                return false;
            }

            if (body.SourceId is not null && !ExternalSourceId().IsMatch(body.SourceId))
            {
                return false;
            }

            if (body.Path is not null
                && (body.Path.Length > AssistantIpc.MaxDocumentPathLength
                    || body.Path.Any(char.IsControl)
                    || !Path.IsPathFullyQualified(body.Path)))
            {
                return false;
            }

            request = body;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static string? SerializeView(View view)
    {
        ArgumentNullException.ThrowIfNull(view);
        var json = JsonSerializer.Serialize(view, Options);
        return TryParseView(json, out _) ? json : null;
    }

    public static bool TryParseView(string? json, out JsonElement view)
    {
        view = default;
        if (string.IsNullOrWhiteSpace(json) || json.Length > MaxViewCharacters)
        {
            return false;
        }

        try
        {
            var body = JsonSerializer.Deserialize<View>(json, Options);
            if (body is null
                || body.Skills is null
                || body.Sources is null
                || body.Skills.Count > MaxSkills
                || body.Sources.Count > MaxSources
                || !IsSafeLine(body.Message, MaxDiagnosticCharacters)
                || !IsSafeLine(body.Diagnostic, MaxDiagnosticCharacters)
                || body.Skills.Any(skill => !IsSkill(skill))
                || body.Sources.Any(source => !IsSource(source))
                || body.Skills.Select(skill => skill.Key).Distinct(StringComparer.Ordinal).Count() != body.Skills.Count
                || (body.Detail is not null && !IsDetail(body.Detail)))
            {
                return false;
            }

            view = JsonSerializer.SerializeToElement(body, Options);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static bool IsKey(string? key)
    {
        return !string.IsNullOrWhiteSpace(key)
            && key.Length <= MaxKeyLength
            && !key.Any(char.IsControl)
            && key.IndexOf('/') > 0;
    }

    /// <summary>A display line that cannot carry a drive or UNC path.</summary>
    public static bool IsSafeLine(string? text, int maxLength)
    {
        return text is null
            || (text.Length <= maxLength
                && !text.Any(char.IsControl)
                && !text.Contains(":\\", StringComparison.Ordinal)
                && !text.Contains("\\\\", StringComparison.Ordinal));
    }

    private static bool IsSkill(SkillView skill)
    {
        return IsKey(skill.Key)
            && IsSafeLine(skill.Name, 80) && !string.IsNullOrWhiteSpace(skill.Name)
            && IsSafeLine(skill.Description, MaxDescriptionCharacters) && skill.Description is not null
            && Sources.Contains(skill.Source, StringComparer.Ordinal)
            && IsSafeLine(skill.SourceId, 40) && !string.IsNullOrWhiteSpace(skill.SourceId)
            && IsSafeLine(skill.SourceLabel, 120) && !string.IsNullOrWhiteSpace(skill.SourceLabel)
            && Statuses.Contains(skill.Status, StringComparer.Ordinal)
            && (skill.Sha256 is null || Sha256().IsMatch(skill.Sha256))
            && IsSafeLine(skill.Diagnostic, MaxDiagnosticCharacters)
            && skill.Files is not null && skill.Files.Count <= 32 && skill.Files.All(file => IsSafeLine(file, 200));
    }

    private static bool IsSource(SourceView source)
    {
        return IsSafeLine(source.Id, 40) && !string.IsNullOrWhiteSpace(source.Id)
            && Sources.Contains(source.Kind, StringComparer.Ordinal)
            && IsSafeLine(source.Label, 120) && !string.IsNullOrWhiteSpace(source.Label)
            && Statuses.Contains(source.Status, StringComparer.Ordinal)
            && source.SkillCount is >= 0 and <= 64
            && IsSafeLine(source.Diagnostic, MaxDiagnosticCharacters)
            && (!source.Removable || source.Kind == "external");
    }

    private static bool IsDetail(DetailView detail)
    {
        return IsKey(detail.Key)
            && IsSafeLine(detail.Name, 80)
            && (detail.Source is null || Sources.Contains(detail.Source, StringComparer.Ordinal))
            && (detail.Sha256 is null || Sha256().IsMatch(detail.Sha256))
            && (detail.Content is null || detail.Content.Length <= MaxDetailCharacters)
            && IsSafeLine(detail.Message, MaxDiagnosticCharacters)
            && detail.Files is not null && detail.Files.Count <= 32 && detail.Files.All(file => IsSafeLine(file, 200));
    }

    [GeneratedRegex("^external-[0-9a-f]{12}$")]
    private static partial Regex ExternalSourceId();

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex Sha256();

    public sealed class Request
    {
        public string? Action { get; set; }

        public string? Key { get; set; }

        public string? SourceId { get; set; }

        public string? Path { get; set; }
    }

    public sealed class View
    {
        public List<SkillView>? Skills { get; set; }

        public List<SourceView>? Sources { get; set; }

        public DetailView? Detail { get; set; }

        public string? Message { get; set; }

        public bool IsError { get; set; }

        public string? Diagnostic { get; set; }
    }

    public sealed class SkillView
    {
        public string? Key { get; set; }

        public string? Name { get; set; }

        public string? Description { get; set; }

        public string? Source { get; set; }

        public string? SourceId { get; set; }

        public string? SourceLabel { get; set; }

        public bool Enabled { get; set; }

        public string? Status { get; set; }

        public string? Sha256 { get; set; }

        public string? Diagnostic { get; set; }

        public List<string>? Files { get; set; }
    }

    public sealed class SourceView
    {
        public string? Id { get; set; }

        public string? Kind { get; set; }

        public string? Label { get; set; }

        public string? Status { get; set; }

        public int SkillCount { get; set; }

        public string? Diagnostic { get; set; }

        public bool Removable { get; set; }
    }

    public sealed class DetailView
    {
        public string? Key { get; set; }

        public string? Name { get; set; }

        public string? Source { get; set; }

        public string? Sha256 { get; set; }

        public string? Content { get; set; }

        public bool Truncated { get; set; }

        public string? Message { get; set; }

        public List<string>? Files { get; set; }
    }
}
