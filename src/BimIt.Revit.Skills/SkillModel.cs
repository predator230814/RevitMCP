namespace BimIt.Revit.Skills;

public enum SkillSourceKind
{
    BuiltIn,
    Custom,
    External,
}

public enum SkillStatus
{
    Ready,
    Disabled,
    Invalid,
    Conflict,
    Unavailable,
}

/// <summary>
/// Bounds for one Skill folder and for the registry. A Skill that exceeds a bound is invalid. Nothing is truncated silently.
/// </summary>
public static class SkillLimits
{
    public const string SkillFileName = "SKILL.md";

    public const int MaxSkillFileBytes = 128 * 1024;

    public const long MaxSkillBytes = 1024 * 1024;

    public const int MaxFiles = 32;

    public const int MaxDepth = 4;

    public const int MaxEntriesScanned = 256;

    public const int MaxNameLength = 64;

    public const int MaxDescriptionLength = 1024;

    public const int MaxFrontMatterCharacters = 8 * 1024;

    public const int MaxRelativePathLength = 200;

    public const int MaxSkillsPerSource = 64;

    public const int MaxExternalSources = 8;

    public const int MaxSourcePathLength = 400;

    public const int MaxModelContentCharacters = 64_000;

    public const int MaxSettingsBytes = 64 * 1024;

    public static readonly IReadOnlyList<string> AllowedExtensions = [".md", ".txt", ".yaml", ".yml", ".json", ".csv"];

    public static bool IsAllowedExtension(string? fileName)
    {
        var extension = Path.GetExtension(fileName ?? "");
        return AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }
}

public static class SkillWire
{
    public static string Source(SkillSourceKind kind) => kind switch
    {
        SkillSourceKind.BuiltIn => "built_in",
        SkillSourceKind.Custom => "custom",
        SkillSourceKind.External => "external",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static string Status(SkillStatus status) => status switch
    {
        SkillStatus.Ready => "ready",
        SkillStatus.Disabled => "disabled",
        SkillStatus.Invalid => "invalid",
        SkillStatus.Conflict => "conflict",
        SkillStatus.Unavailable => "unavailable",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };
}

/// <summary>One validated file inside a Skill folder. The relative path uses '/' and never leaves the folder.</summary>
public sealed record SkillFile(string RelativePath, byte[] Bytes, string Text);

/// <summary>A Skill folder that passed every bound. The bytes are the ones hashed.</summary>
public sealed record SkillPackage(string Name, string Description, IReadOnlyList<SkillFile> Files, string Sha256)
{
    public SkillFile SkillFile => Files.First(file => file.RelativePath == SkillLimits.SkillFileName);
}

/// <summary>
/// One Skill as the registry sees it. <see cref="Directory"/> is internal so a path cannot reach a view, the model, or the WebView.
/// </summary>
public sealed record SkillEntry(
    string Key,
    string Name,
    string Description,
    SkillSourceKind Source,
    string SourceId,
    string SourceLabel,
    bool Enabled,
    SkillStatus Status,
    string? Sha256,
    string? Diagnostic,
    IReadOnlyList<string> Files)
{
    internal string Directory { get; init; } = "";
}

public sealed record SkillSourceView(
    string Id,
    SkillSourceKind Kind,
    string Label,
    SkillStatus Status,
    int SkillCount,
    string? Diagnostic);

/// <summary>What the model may see before it loads a Skill.</summary>
public sealed record SkillSummary(string Name, string Description, SkillSourceKind Source);

public sealed record SkillCatalog(IReadOnlyList<SkillEntry> Entries, IReadOnlyList<SkillSourceView> Sources, string? Diagnostic)
{
    public static SkillCatalog Empty { get; } = new([], [], null);

    public IReadOnlyList<SkillSummary> ReadySummaries()
    {
        return Entries
            .Where(entry => entry.Status == SkillStatus.Ready)
            .Select(entry => new SkillSummary(entry.Name, entry.Description, entry.Source))
            .ToArray();
    }
}

public sealed record SkillCommandResult(bool Ok, string Message, SkillCatalog Catalog);

public static class SkillReadCodes
{
    public const string Ok = "ok";
    public const string UnknownSkill = "unknown_skill";
    public const string NotReady = "skill_not_ready";
    public const string InvalidFile = "invalid_file";
    public const string FileNotFound = "file_not_found";
    public const string Unavailable = "skill_unavailable";
}

/// <summary>Bounded text returned by a Skill read. It carries the source kind and hash, never a filesystem path.</summary>
public sealed record SkillReadResult(
    string Code,
    string Message,
    string? Name = null,
    SkillSourceKind? Source = null,
    string? Sha256 = null,
    string? File = null,
    string? Content = null,
    bool Truncated = false,
    IReadOnlyList<string>? Files = null)
{
    public bool Ok => Code == SkillReadCodes.Ok;
}
