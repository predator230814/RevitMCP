using System.Text.Json;
using System.Text.Json.Serialization;

namespace BimIt.Revit.Skills;

public sealed class SkillSettings
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("external_sources")]
    public List<string> ExternalSources { get; set; } = [];

    [JsonPropertyName("disabled")]
    public List<string> Disabled { get; set; } = [];
}

/// <summary>
/// Local user settings for Skills: configured external folders and disabled Skill keys. The file holds no Skill
/// content. A file that cannot be read leaves the defaults in force and is not overwritten until the user changes a
/// setting.
/// </summary>
public sealed class SkillSettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
    };

    private readonly string _path;

    public SkillSettingsStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
    }

    public string? LoadDiagnostic { get; private set; }

    public SkillSettings Load()
    {
        LoadDiagnostic = null;
        try
        {
            var info = new FileInfo(_path);
            if (!info.Exists)
            {
                return new SkillSettings();
            }

            if (info.Length > SkillLimits.MaxSettingsBytes)
            {
                LoadDiagnostic = "The Skill settings file is too large. Defaults are in use.";
                return new SkillSettings();
            }

            var settings = JsonSerializer.Deserialize<SkillSettings>(File.ReadAllText(_path), Options) ?? new SkillSettings();
            settings.ExternalSources = (settings.ExternalSources ?? [])
                .Where(path => !string.IsNullOrWhiteSpace(path) && path.Length <= SkillLimits.MaxSourcePathLength)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(SkillLimits.MaxExternalSources)
                .ToList();
            settings.Disabled = (settings.Disabled ?? [])
                .Where(key => !string.IsNullOrWhiteSpace(key) && key.Length <= 160)
                .Distinct(StringComparer.Ordinal)
                .Take(1024)
                .ToList();
            return settings;
        }
        catch (Exception exception) when (exception is JsonException || SkillPackageReader.IsFileSystemFailure(exception))
        {
            LoadDiagnostic = "The Skill settings file could not be read. Defaults are in use.";
            return new SkillSettings();
        }
    }

    public void Save(SkillSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var directory = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, Options));
        File.Move(temporary, _path, overwrite: true);
    }
}
