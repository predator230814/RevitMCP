using System.Security.Cryptography;
using System.Text;

namespace BimIt.Revit.Skills;

/// <param name="BuiltInRoot">Read-only Skills shipped with BIM-IT.</param>
/// <param name="CustomRoot">User-local Skills, one subfolder per Skill.</param>
/// <param name="SettingsPath">Local settings file for external sources and disabled Skills.</param>
public sealed record SkillRegistryOptions(string BuiltInRoot, string CustomRoot, string SettingsPath)
{
    public const string BuiltInSourceId = "built-in";

    public const string CustomSourceId = "custom";

    /// <summary>Defaults under %APPDATA%\BIM-IT\skills\. The built-in root ships beside the executable.</summary>
    public static SkillRegistryOptions CreateDefault(string builtInRoot)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var root = Path.Combine(appData, "BIM-IT", "skills");
        return new SkillRegistryOptions(builtInRoot, Path.Combine(root, "custom"), Path.Combine(root, "settings.json"));
    }
}

/// <summary>
/// Provider-neutral Skill registry. It scans three kinds of source, validates each Skill folder, records a content
/// hash, and resolves conflicts visibly: two enabled Skills with the same name are both held back until the user
/// disables or removes one. No source outranks another. The registry never executes Skill content and never
/// returns a filesystem path to a caller-facing view.
/// </summary>
public sealed class SkillRegistry
{
    private readonly object _gate = new();
    private readonly SkillRegistryOptions _options;
    private readonly SkillSettingsStore _settings;
    private SkillCatalog _catalog = SkillCatalog.Empty;

    public SkillRegistry(SkillRegistryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = new SkillRegistryOptions(
            Path.GetFullPath(options.BuiltInRoot),
            Path.GetFullPath(options.CustomRoot),
            Path.GetFullPath(options.SettingsPath));
        _settings = new SkillSettingsStore(_options.SettingsPath);
    }

    public SkillCatalog Current
    {
        get
        {
            lock (_gate)
            {
                return _catalog;
            }
        }
    }

    /// <summary>The custom Skills folder. For opening in Explorer only. It is not sent to a model or the WebView.</summary>
    public string CustomRoot => _options.CustomRoot;

    public SkillCatalog Reload()
    {
        lock (_gate)
        {
            _catalog = Scan(_settings.Load(), _settings.LoadDiagnostic);
            return _catalog;
        }
    }

    public SkillCommandResult SetEnabled(string key, bool enabled)
    {
        lock (_gate)
        {
            var catalog = Scan(_settings.Load(), _settings.LoadDiagnostic);
            var entry = catalog.Entries.FirstOrDefault(candidate => candidate.Key == key);
            if (entry is null)
            {
                _catalog = catalog;
                return new SkillCommandResult(false, "That Skill is no longer available. Reload and try again.", catalog);
            }

            var settings = _settings.Load();
            settings.Disabled.RemoveAll(value => value == key);
            if (!enabled)
            {
                settings.Disabled.Add(key);
            }

            if (!TrySave(settings, out var failure))
            {
                _catalog = catalog;
                return new SkillCommandResult(false, failure, catalog);
            }

            _catalog = Scan(settings, null);
            return new SkillCommandResult(true, (enabled ? "Enabled " : "Disabled ") + entry.Name + ".", _catalog);
        }
    }

    public SkillCommandResult AddExternalSource(string path)
    {
        lock (_gate)
        {
            var settings = _settings.Load();
            if (!TryNormalizeFolder(path, out var full))
            {
                _catalog = Scan(settings, _settings.LoadDiagnostic);
                return new SkillCommandResult(false, "Choose an existing folder.", _catalog);
            }

            if (IsWithin(full, _options.BuiltInRoot) || IsWithin(full, _options.CustomRoot)
                || IsWithin(_options.BuiltInRoot, full) || IsWithin(_options.CustomRoot, full))
            {
                _catalog = Scan(settings, _settings.LoadDiagnostic);
                return new SkillCommandResult(false, "That folder overlaps the built-in or custom Skills folder.", _catalog);
            }

            if (settings.ExternalSources.Any(existing => IsWithin(full, existing) || IsWithin(existing, full)))
            {
                _catalog = Scan(settings, _settings.LoadDiagnostic);
                return new SkillCommandResult(false, "That folder is already a Skills source or overlaps one.", _catalog);
            }

            if (settings.ExternalSources.Count >= SkillLimits.MaxExternalSources)
            {
                _catalog = Scan(settings, _settings.LoadDiagnostic);
                return new SkillCommandResult(false, "At most " + SkillLimits.MaxExternalSources + " external Skills folders can be added.", _catalog);
            }

            settings.ExternalSources.Add(full);
            if (!TrySave(settings, out var failure))
            {
                settings.ExternalSources.Remove(full);
                _catalog = Scan(settings, null);
                return new SkillCommandResult(false, failure, _catalog);
            }

            _catalog = Scan(settings, null);
            var source = _catalog.Sources.FirstOrDefault(view => view.Id == ExternalSourceId(full));
            return new SkillCommandResult(true, "Added Skills folder " + (source?.Label ?? "") + ".", _catalog);
        }
    }

    public SkillCommandResult RemoveExternalSource(string sourceId)
    {
        lock (_gate)
        {
            var settings = _settings.Load();
            var match = settings.ExternalSources.FirstOrDefault(path => ExternalSourceId(path) == sourceId);
            if (match is null)
            {
                _catalog = Scan(settings, _settings.LoadDiagnostic);
                return new SkillCommandResult(false, "That Skills folder is not configured.", _catalog);
            }

            settings.ExternalSources.Remove(match);
            settings.Disabled.RemoveAll(key => key.StartsWith(sourceId + "/", StringComparison.Ordinal));
            if (!TrySave(settings, out var failure))
            {
                _catalog = Scan(_settings.Load(), null);
                return new SkillCommandResult(false, failure, _catalog);
            }

            _catalog = Scan(settings, null);
            return new SkillCommandResult(true, "Removed the Skills folder. Its files were not changed.", _catalog);
        }
    }

    /// <summary>
    /// Validates one folder that contains SKILL.md, then copies exactly the validated bytes into the custom source.
    /// Hidden dot entries are not copied. An existing custom Skill is never overwritten.
    /// </summary>
    public SkillCommandResult Import(string path)
    {
        lock (_gate)
        {
            var settings = _settings.Load();
            if (!TryNormalizeFolder(path, out var full))
            {
                _catalog = Scan(settings, _settings.LoadDiagnostic);
                return new SkillCommandResult(false, "Choose an existing folder that contains SKILL.md.", _catalog);
            }

            if (IsWithin(full, _options.CustomRoot) || IsWithin(full, _options.BuiltInRoot))
            {
                _catalog = Scan(settings, _settings.LoadDiagnostic);
                return new SkillCommandResult(false, "That Skill is already installed.", _catalog);
            }

            if (!File.Exists(Path.Combine(full, SkillLimits.SkillFileName)))
            {
                _catalog = Scan(settings, _settings.LoadDiagnostic);
                return new SkillCommandResult(false, "Choose a folder that contains SKILL.md.", _catalog);
            }

            var read = SkillPackageReader.Read(full, expectedName: null);
            if (read.Package is null)
            {
                _catalog = Scan(settings, _settings.LoadDiagnostic);
                return new SkillCommandResult(false, "The Skill was not imported. " + read.Diagnostic, _catalog);
            }

            var target = Path.Combine(_options.CustomRoot, read.Package.Name);
            if (Directory.Exists(target) || File.Exists(target))
            {
                _catalog = Scan(settings, _settings.LoadDiagnostic);
                return new SkillCommandResult(false, "A custom Skill named " + read.Package.Name + " already exists. Remove it first.", _catalog);
            }

            var staging = Path.Combine(_options.CustomRoot, ".import-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(_options.CustomRoot);
                foreach (var file in read.Package.Files)
                {
                    var destination = Path.GetFullPath(Path.Combine(staging, file.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
                    if (!IsWithin(destination, staging))
                    {
                        throw new IOException("escape");
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.WriteAllBytes(destination, file.Bytes);
                }

                Directory.Move(staging, target);
            }
            catch (Exception exception) when (SkillPackageReader.IsFileSystemFailure(exception))
            {
                TryDelete(staging);
                _catalog = Scan(settings, _settings.LoadDiagnostic);
                return new SkillCommandResult(false, "The Skill could not be copied into the custom Skills folder.", _catalog);
            }

            _catalog = Scan(settings, _settings.LoadDiagnostic);
            var imported = _catalog.Entries.FirstOrDefault(entry => entry.SourceId == SkillRegistryOptions.CustomSourceId && entry.Name == read.Package.Name);
            var message = imported?.Status switch
            {
                SkillStatus.Ready => "Imported " + read.Package.Name + ". It is available on the next turn.",
                SkillStatus.Conflict => "Imported " + read.Package.Name + ", but another enabled Skill has the same name. Disable one to resolve the conflict.",
                _ => "Imported " + read.Package.Name + ".",
            };
            return new SkillCommandResult(true, message, _catalog);
        }
    }

    /// <summary>The full SKILL.md text and file list for Settings. Built-in content is shown read-only.</summary>
    public SkillReadResult Detail(string key)
    {
        SkillEntry? entry;
        lock (_gate)
        {
            entry = _catalog.Entries.FirstOrDefault(candidate => candidate.Key == key);
        }

        if (entry is null)
        {
            return new SkillReadResult(SkillReadCodes.UnknownSkill, "That Skill is no longer available.");
        }

        if (entry.Status == SkillStatus.Invalid)
        {
            return new SkillReadResult(SkillReadCodes.NotReady, entry.Diagnostic ?? "The Skill is invalid.", entry.Name, entry.Source);
        }

        return ReadFile(entry, SkillLimits.SkillFileName, requireReady: false);
    }

    /// <summary>
    /// The model-facing read. Only a ready Skill is readable, and only one of its own validated files. The Skill is
    /// validated again from disk before any content is returned.
    /// </summary>
    public SkillReadResult ReadForModel(string? name, string? file)
    {
        if (!SkillPackageReader.IsValidName(name))
        {
            return new SkillReadResult(SkillReadCodes.UnknownSkill, "Unknown Skill. Use a name from the available Skill list.");
        }

        SkillEntry[] matches;
        lock (_gate)
        {
            matches = _catalog.Entries.Where(entry => entry.Name == name).ToArray();
        }

        if (matches.Length == 0)
        {
            return new SkillReadResult(SkillReadCodes.UnknownSkill, "Unknown Skill. Use a name from the available Skill list.");
        }

        var ready = matches.Where(entry => entry.Status == SkillStatus.Ready).ToArray();
        if (ready.Length != 1)
        {
            return new SkillReadResult(SkillReadCodes.NotReady, "The Skill " + name + " is not available. It is disabled, invalid, or in conflict.", name);
        }

        if (!TryRelative(file, out var relative))
        {
            return new SkillReadResult(SkillReadCodes.InvalidFile, "The file must be a relative path inside the Skill, with an allowed extension.", name, ready[0].Source);
        }

        return ReadFile(ready[0], relative, requireReady: true);
    }

    internal static string ExternalSourceId(string path)
    {
        var normalized = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return "external-" + Convert.ToHexString(hash, 0, 6).ToLowerInvariant();
    }

    internal static bool TryRelative(string? file, out string relative)
    {
        relative = SkillLimits.SkillFileName;
        if (string.IsNullOrWhiteSpace(file))
        {
            return true;
        }

        var candidate = file.Trim().Replace('\\', '/');
        if (candidate.Length > SkillLimits.MaxRelativePathLength
            || candidate.StartsWith('/')
            || candidate.Contains(':', StringComparison.Ordinal)
            || Path.IsPathRooted(candidate)
            || candidate.Any(char.IsControl)
            || !SkillLimits.IsAllowedExtension(candidate))
        {
            return false;
        }

        var segments = candidate.Split('/');
        if (segments.Length > SkillLimits.MaxDepth + 1
            || segments.Any(segment => segment.Length == 0 || segment == "." || segment == ".." || segment.StartsWith('.')))
        {
            return false;
        }

        relative = candidate;
        return true;
    }

    private SkillReadResult ReadFile(SkillEntry entry, string relative, bool requireReady)
    {
        var read = SkillPackageReader.Read(entry.Directory, entry.Name);
        if (read.Package is null)
        {
            return new SkillReadResult(SkillReadCodes.Unavailable, "The Skill " + entry.Name + " changed and is no longer valid. Reload Skills.", entry.Name, entry.Source);
        }

        if (requireReady && entry.Status != SkillStatus.Ready)
        {
            return new SkillReadResult(SkillReadCodes.NotReady, "The Skill " + entry.Name + " is not available.", entry.Name, entry.Source);
        }

        var target = read.Package.Files.FirstOrDefault(candidate => string.Equals(candidate.RelativePath, relative, StringComparison.OrdinalIgnoreCase));
        var files = read.Package.Files.Select(candidate => candidate.RelativePath).ToArray();
        if (target is null)
        {
            return new SkillReadResult(SkillReadCodes.FileNotFound, "The Skill " + entry.Name + " has no file " + relative + ".", entry.Name, entry.Source, read.Package.Sha256, Files: files);
        }

        var content = target.Text;
        var truncated = content.Length > SkillLimits.MaxModelContentCharacters;
        if (truncated)
        {
            content = content[..SkillLimits.MaxModelContentCharacters];
        }

        return new SkillReadResult(
            SkillReadCodes.Ok,
            "Read " + entry.Name + "/" + target.RelativePath + ".",
            entry.Name,
            entry.Source,
            read.Package.Sha256,
            target.RelativePath,
            content,
            truncated,
            files);
    }

    private SkillCatalog Scan(SkillSettings settings, string? settingsDiagnostic)
    {
        var disabled = new HashSet<string>(settings.Disabled, StringComparer.Ordinal);
        var entries = new List<SkillEntry>();
        var sources = new List<(SkillSourceView View, List<SkillEntry> Entries)>();

        sources.Add(ScanSource(SkillSourceKind.BuiltIn, SkillRegistryOptions.BuiltInSourceId, "Built-in", _options.BuiltInRoot, disabled, create: false));
        sources.Add(ScanSource(SkillSourceKind.Custom, SkillRegistryOptions.CustomSourceId, "Custom", _options.CustomRoot, disabled, create: true));
        var labels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in settings.ExternalSources)
        {
            var label = Leaf(path);
            labels[label] = labels.TryGetValue(label, out var seen) ? seen + 1 : 1;
            if (labels[label] > 1)
            {
                label += " (" + labels[label] + ")";
            }

            sources.Add(ScanSource(SkillSourceKind.External, ExternalSourceId(path), label, path, disabled, create: false));
        }

        foreach (var (_, sourceEntries) in sources)
        {
            entries.AddRange(sourceEntries);
        }

        var conflicts = entries
            .Where(entry => entry.Status == SkillStatus.Ready)
            .GroupBy(entry => entry.Name, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            if (entry.Status != SkillStatus.Ready || !conflicts.TryGetValue(entry.Name, out var group))
            {
                continue;
            }

            var others = group.Where(other => other.Key != entry.Key).Select(other => other.SourceLabel).Distinct(StringComparer.Ordinal);
            entries[index] = entry with
            {
                Status = SkillStatus.Conflict,
                Diagnostic = "Another enabled Skill named " + entry.Name + " exists in " + string.Join(", ", others)
                    + ". Neither is used until one is disabled or removed.",
            };
        }

        var views = sources.Select(source => source.View).ToArray();
        return new SkillCatalog(entries, views, settingsDiagnostic);
    }

    private (SkillSourceView View, List<SkillEntry> Entries) ScanSource(
        SkillSourceKind kind,
        string sourceId,
        string label,
        string root,
        HashSet<string> disabled,
        bool create)
    {
        var entries = new List<SkillEntry>();
        string[] directories;
        try
        {
            if (create)
            {
                Directory.CreateDirectory(root);
            }

            if (!Directory.Exists(root))
            {
                return (new SkillSourceView(sourceId, kind, label, SkillStatus.Unavailable, 0, "The Skills folder is not available."), entries);
            }

            directories = Directory.EnumerateDirectories(root)
                .Where(directory => !Leaf(directory).StartsWith('.'))
                .OrderBy(directory => Leaf(directory), StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception exception) when (SkillPackageReader.IsFileSystemFailure(exception))
        {
            return (new SkillSourceView(sourceId, kind, label, SkillStatus.Unavailable, 0, "The Skills folder could not be read."), entries);
        }

        string? diagnostic = null;
        foreach (var directory in directories)
        {
            if (entries.Count >= SkillLimits.MaxSkillsPerSource)
            {
                diagnostic = "Only the first " + SkillLimits.MaxSkillsPerSource + " Skills in this folder are listed.";
                break;
            }

            var folder = Leaf(directory);
            var key = sourceId + "/" + (SkillPackageReader.IsValidName(folder) ? folder : "#" + ShortHash(folder));
            var display = SkillPackageReader.IsValidName(folder) ? folder : SafeFolderLabel(folder);
            var enabled = !disabled.Contains(key);
            DirectoryInfo info;
            bool hasSkillFile;
            try
            {
                info = new DirectoryInfo(directory);
                if (SkillPackageReader.IsLink(info))
                {
                    entries.Add(Entry(key, display, "", kind, sourceId, label, enabled, SkillStatus.Invalid, null, "The Skill folder is a link. Links are not followed.", [], directory));
                    continue;
                }

                hasSkillFile = File.Exists(Path.Combine(directory, SkillLimits.SkillFileName));
            }
            catch (Exception exception) when (SkillPackageReader.IsFileSystemFailure(exception))
            {
                entries.Add(Entry(key, display, "", kind, sourceId, label, enabled, SkillStatus.Invalid, null, "The Skill folder could not be read.", [], directory));
                continue;
            }

            if (!hasSkillFile)
            {
                // A folder without SKILL.md is not a Skill, for example a shared references folder.
                continue;
            }

            var read = SkillPackageReader.Read(directory, SkillPackageReader.IsValidName(folder) ? folder : "");
            if (read.Package is null)
            {
                entries.Add(Entry(key, read.Name ?? display, "", kind, sourceId, label, enabled, SkillStatus.Invalid, null, read.Diagnostic, [], directory));
                continue;
            }

            entries.Add(Entry(
                key,
                read.Package.Name,
                read.Package.Description,
                kind,
                sourceId,
                label,
                enabled,
                enabled ? SkillStatus.Ready : SkillStatus.Disabled,
                read.Package.Sha256,
                null,
                read.Package.Files.Select(file => file.RelativePath).ToArray(),
                directory));
        }

        return (new SkillSourceView(sourceId, kind, label, SkillStatus.Ready, entries.Count, diagnostic), entries);
    }

    private static SkillEntry Entry(
        string key,
        string name,
        string description,
        SkillSourceKind kind,
        string sourceId,
        string label,
        bool enabled,
        SkillStatus status,
        string? sha256,
        string? diagnostic,
        IReadOnlyList<string> files,
        string directory)
    {
        return new SkillEntry(key, name, description, kind, sourceId, label, enabled, status, sha256, diagnostic, files) { Directory = directory };
    }

    private bool TrySave(SkillSettings settings, out string failure)
    {
        failure = "";
        try
        {
            _settings.Save(settings);
            return true;
        }
        catch (Exception exception) when (SkillPackageReader.IsFileSystemFailure(exception))
        {
            failure = "The Skill settings could not be saved.";
            return false;
        }
    }

    private static bool TryNormalizeFolder(string? path, out string full)
    {
        full = "";
        if (string.IsNullOrWhiteSpace(path) || path.Length > SkillLimits.MaxSourcePathLength || !Path.IsPathFullyQualified(path))
        {
            return false;
        }

        try
        {
            full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
            if (full.EndsWith(':'))
            {
                full += Path.DirectorySeparatorChar;
            }

            return Directory.Exists(full);
        }
        catch (Exception exception) when (SkillPackageReader.IsFileSystemFailure(exception))
        {
            return false;
        }
    }

    private static bool IsWithin(string path, string root)
    {
        var normalizedPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static string Leaf(string path)
    {
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var leaf = Path.GetFileName(trimmed);
        return string.IsNullOrEmpty(leaf) ? trimmed : leaf;
    }

    private static string SafeFolderLabel(string folder)
    {
        var clean = new string(folder.Where(character => !char.IsControl(character)).Take(SkillLimits.MaxNameLength).ToArray());
        return string.IsNullOrWhiteSpace(clean) ? "(unnamed folder)" : clean;
    }

    private static string ShortHash(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)), 0, 6).ToLowerInvariant();
    }

    private static void TryDelete(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception exception) when (SkillPackageReader.IsFileSystemFailure(exception))
        {
        }
    }
}
