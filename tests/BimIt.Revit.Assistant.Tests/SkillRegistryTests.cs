using System.Diagnostics;
using System.Text;
using BimIt.Revit.Skills;
using Xunit;

namespace BimIt.Revit.Assistant.Tests;

public sealed class SkillRegistryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bimit-skills-" + Guid.NewGuid().ToString("N"));

    public SkillRegistryTests()
    {
        Directory.CreateDirectory(BuiltIn);
        Directory.CreateDirectory(Custom);
    }

    private string BuiltIn => Path.Combine(_root, "built-in");

    private string Custom => Path.Combine(_root, "custom");

    private string Settings => Path.Combine(_root, "settings.json");

    public void Dispose()
    {
        try
        {
            // Remove links first so recursive deletion never walks into a junction target.
            foreach (var link in Directory.EnumerateDirectories(_root, "*", SearchOption.AllDirectories)
                .Where(directory => new DirectoryInfo(directory).LinkTarget is not null)
                .ToArray())
            {
                Directory.Delete(link);
            }

            Directory.Delete(_root, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public void Valid_skill_loads_from_each_source_with_provenance()
    {
        WriteSkill(BuiltIn, "alpha-query");
        WriteSkill(Custom, "beta-layout");
        var external = Path.Combine(_root, "firm");
        WriteSkill(external, "gamma-standards");
        var registry = Registry();
        Assert.True(registry.AddExternalSource(external).Ok);

        var catalog = registry.Reload();

        var alpha = Single(catalog, "alpha-query");
        Assert.Equal(SkillSourceKind.BuiltIn, alpha.Source);
        Assert.Equal(SkillRegistryOptions.BuiltInSourceId, alpha.SourceId);
        Assert.Equal(SkillStatus.Ready, alpha.Status);
        Assert.True(alpha.Enabled);
        Assert.Matches("^[0-9a-f]{64}$", alpha.Sha256);
        Assert.Equal("Use when testing alpha-query.", alpha.Description);

        Assert.Equal(SkillSourceKind.Custom, Single(catalog, "beta-layout").Source);
        var gamma = Single(catalog, "gamma-standards");
        Assert.Equal(SkillSourceKind.External, gamma.Source);
        Assert.StartsWith("external-", gamma.SourceId, StringComparison.Ordinal);
        Assert.Equal("firm", gamma.SourceLabel);
        Assert.Equal(3, catalog.ReadySummaries().Count);
    }

    [Fact]
    public void Front_matter_supports_quoted_folded_and_nested_values()
    {
        var parsed = Parse("---\nname: \"quoted-name\"\ndescription: >\n  Folded line one\n  and line two.\nlicense: 'Apache 2.0'\nmetadata:\n  author: firm\n  version: \"1\"\nallowed-tools: [a, b]\n---\n# Body\n");
        Assert.Equal("quoted-name", parsed.Values["name"]);
        Assert.Equal("Folded line one and line two.", parsed.Values["description"]);
        Assert.Equal("Apache 2.0", parsed.Values["license"]);
        Assert.Null(parsed.Values["metadata"]);
        Assert.Contains("# Body", parsed.Body, StringComparison.Ordinal);

        Assert.False(SkillFrontMatter.TryParse("# no front matter", out _, out _));
        Assert.False(SkillFrontMatter.TryParse("---\nname: a\n", out _, out _));
        Assert.False(SkillFrontMatter.TryParse("---\nname: a\nname: b\n---\n", out _, out _));
        Assert.False(SkillFrontMatter.TryParse("---\nname: \"open\n---\n", out _, out _));
    }

    [Theory]
    [InlineData("Upper-Case")]
    [InlineData("-leading")]
    [InlineData("trailing-")]
    [InlineData("double--hyphen")]
    [InlineData("under_score")]
    public void Invalid_name_is_reported_and_not_ready(string name)
    {
        var folder = Path.Combine(Custom, "valid-folder");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "SKILL.md"), "---\nname: " + name + "\ndescription: x\n---\nbody");

        var entry = Assert.Single(Registry().Reload().Entries);
        Assert.Equal(SkillStatus.Invalid, entry.Status);
        Assert.NotNull(entry.Diagnostic);
        Assert.Empty(Registry().Reload().ReadySummaries());
    }

    [Fact]
    public void Name_longer_than_64_or_not_matching_folder_is_invalid()
    {
        var longName = new string('a', 65);
        var folder = Path.Combine(Custom, "short");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "SKILL.md"), "---\nname: " + longName + "\ndescription: x\n---\n");
        WriteSkill(Custom, "folder-name", nameOverride: "other-name");

        var catalog = Registry().Reload();
        Assert.All(catalog.Entries, entry => Assert.Equal(SkillStatus.Invalid, entry.Status));
        Assert.Contains(catalog.Entries, entry => entry.Diagnostic!.Contains("does not match", StringComparison.Ordinal));
    }

    [Fact]
    public void Missing_description_is_invalid_with_a_bounded_diagnostic()
    {
        var folder = Path.Combine(Custom, "no-description");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "SKILL.md"), "---\nname: no-description\n---\nbody");

        var entry = Assert.Single(Registry().Reload().Entries);
        Assert.Equal(SkillStatus.Invalid, entry.Status);
        Assert.Equal("The front matter has no description.", entry.Diagnostic);
        Assert.DoesNotContain(_root, entry.Diagnostic!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Duplicate_names_conflict_visibly_and_neither_becomes_active()
    {
        WriteSkill(BuiltIn, "electrical-layout");
        WriteSkill(Custom, "electrical-layout", body: "custom version");
        var registry = Registry();

        var catalog = registry.Reload();
        var conflicts = catalog.Entries.Where(entry => entry.Name == "electrical-layout").ToArray();
        Assert.Equal(2, conflicts.Length);
        Assert.All(conflicts, entry => Assert.Equal(SkillStatus.Conflict, entry.Status));
        Assert.All(conflicts, entry => Assert.Contains("Neither is used", entry.Diagnostic!, StringComparison.Ordinal));
        Assert.Empty(catalog.ReadySummaries());
        Assert.Equal(SkillReadCodes.NotReady, registry.ReadForModel("electrical-layout", null).Code);

        // Disabling one resolves the conflict explicitly. No source silently wins.
        var custom = conflicts.Single(entry => entry.Source == SkillSourceKind.Custom);
        Assert.True(registry.SetEnabled(custom.Key, false).Ok);
        var resolved = registry.Reload();
        Assert.Equal(SkillStatus.Ready, resolved.Entries.Single(entry => entry.Source == SkillSourceKind.BuiltIn).Status);
        Assert.Equal(SkillStatus.Disabled, resolved.Entries.Single(entry => entry.Source == SkillSourceKind.Custom).Status);
    }

    [Fact]
    public void Disable_and_enable_persist_in_settings()
    {
        WriteSkill(Custom, "toggle-me");
        var registry = Registry();
        var key = Single(registry.Reload(), "toggle-me").Key;

        Assert.True(registry.SetEnabled(key, false).Ok);
        var reopened = Registry();
        var entry = Single(reopened.Reload(), "toggle-me");
        Assert.False(entry.Enabled);
        Assert.Equal(SkillStatus.Disabled, entry.Status);
        Assert.Empty(reopened.Current.ReadySummaries());
        Assert.Contains(key, File.ReadAllText(Settings), StringComparison.Ordinal);

        Assert.True(reopened.SetEnabled(key, true).Ok);
        Assert.Equal(SkillStatus.Ready, Single(Registry().Reload(), "toggle-me").Status);
    }

    [Fact]
    public void Hash_is_stable_and_changes_with_content_files_and_names()
    {
        var folder = WriteSkill(Custom, "hashed");
        var registry = Registry();
        var first = Single(registry.Reload(), "hashed").Sha256;
        Assert.Equal(first, Single(registry.Reload(), "hashed").Sha256);

        File.AppendAllText(Path.Combine(folder, "SKILL.md"), "\nmore");
        var edited = Single(registry.Reload(), "hashed").Sha256;
        Assert.NotEqual(first, edited);

        Directory.CreateDirectory(Path.Combine(folder, "references"));
        File.WriteAllText(Path.Combine(folder, "references", "rules.md"), "rules");
        var added = Single(registry.Reload(), "hashed").Sha256;
        Assert.NotEqual(edited, added);

        File.Move(Path.Combine(folder, "references", "rules.md"), Path.Combine(folder, "references", "renamed.md"));
        Assert.NotEqual(added, Single(registry.Reload(), "hashed").Sha256);
    }

    [Theory]
    [InlineData("../other/SKILL.md")]
    [InlineData("references/../../escape.md")]
    [InlineData("..\\escape.md")]
    [InlineData("C:\\Windows\\win.ini")]
    [InlineData("/etc/passwd.txt")]
    [InlineData("\\\\server\\share\\x.md")]
    [InlineData("references/script.py")]
    [InlineData("references/tool.ps1")]
    [InlineData(".hidden/secret.md")]
    public void Model_read_rejects_traversal_absolute_and_disallowed_files(string file)
    {
        WriteSkill(Custom, "guarded");
        var registry = Registry();
        registry.Reload();

        var read = registry.ReadForModel("guarded", file);

        Assert.False(read.Ok);
        Assert.Equal(SkillReadCodes.InvalidFile, read.Code);
        Assert.Null(read.Content);
    }

    [Fact]
    public void Reference_file_reads_are_bounded_to_the_selected_skill()
    {
        var folder = WriteSkill(Custom, "with-references");
        Directory.CreateDirectory(Path.Combine(folder, "references"));
        File.WriteAllText(Path.Combine(folder, "references", "rules.md"), "Rule one.");
        WriteSkill(Custom, "neighbour", body: "neighbour secret");
        var registry = Registry();
        registry.Reload();

        var skill = registry.ReadForModel("with-references", null);
        Assert.True(skill.Ok);
        Assert.Equal("SKILL.md", skill.File);
        Assert.Contains("references/rules.md", skill.Files!);
        Assert.Equal(SkillSourceKind.Custom, skill.Source);

        var reference = registry.ReadForModel("with-references", "references/rules.md");
        Assert.True(reference.Ok);
        Assert.Equal("Rule one.", reference.Content);
        Assert.Equal(skill.Sha256, reference.Sha256);

        Assert.Equal(SkillReadCodes.FileNotFound, registry.ReadForModel("with-references", "references/missing.md").Code);
        Assert.Equal(SkillReadCodes.InvalidFile, registry.ReadForModel("with-references", "../neighbour/SKILL.md").Code);
        Assert.Equal(SkillReadCodes.UnknownSkill, registry.ReadForModel("not-installed", null).Code);
        Assert.Equal(SkillReadCodes.UnknownSkill, registry.ReadForModel("..", null).Code);
        Assert.Equal(SkillReadCodes.UnknownSkill, registry.ReadForModel(Custom, null).Code);
    }

    [Fact]
    public void Model_read_never_returns_a_filesystem_path()
    {
        WriteSkill(Custom, "pathless");
        var registry = Registry();
        registry.Reload();

        var read = registry.ReadForModel("pathless", null);
        var shaped = System.Text.Json.JsonSerializer.Serialize(BimItSkillTool.Shape(read));

        Assert.DoesNotContain(_root.Replace("\\", "\\\\", StringComparison.Ordinal), shaped, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(":\\\\", shaped, StringComparison.Ordinal);
        Assert.Contains("\"source\":\"custom\"", shaped, StringComparison.Ordinal);
        Assert.Contains("\"sha256\":", shaped, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("run.py")]
    [InlineData("install.ps1")]
    [InlineData("helper.js")]
    [InlineData("tool.dll")]
    [InlineData("setup.sh")]
    [InlineData("payload.exe")]
    [InlineData("LICENSE")]
    public void Executable_or_unknown_file_types_make_the_skill_invalid(string fileName)
    {
        var folder = WriteSkill(Custom, "has-code");
        File.WriteAllText(Path.Combine(folder, fileName), "echo hi");

        var entry = Single(Registry().Reload(), "has-code");
        Assert.Equal(SkillStatus.Invalid, entry.Status);
        Assert.Contains("unsupported file type", entry.Diagnostic!, StringComparison.Ordinal);
    }

    [Fact]
    public void Allowed_text_types_load()
    {
        var folder = WriteSkill(Custom, "text-types");
        foreach (var extension in new[] { ".txt", ".yaml", ".yml", ".json", ".csv", ".md" })
        {
            File.WriteAllText(Path.Combine(folder, "data" + extension), "x");
        }

        Assert.Equal(SkillStatus.Ready, Single(Registry().Reload(), "text-types").Status);
    }

    [Fact]
    public void Size_file_count_and_depth_bounds_fail_closed()
    {
        var big = WriteSkill(Custom, "big-skill-file");
        File.WriteAllText(Path.Combine(big, "SKILL.md"), Front("big-skill-file") + new string('x', SkillLimits.MaxSkillFileBytes));

        var many = WriteSkill(Custom, "many-files");
        for (var index = 0; index < SkillLimits.MaxFiles; index++)
        {
            File.WriteAllText(Path.Combine(many, "f" + index + ".md"), "x");
        }

        var heavy = WriteSkill(Custom, "heavy-skill");
        for (var index = 0; index < 9; index++)
        {
            File.WriteAllText(Path.Combine(heavy, "chunk" + index + ".txt"), new string('y', 120 * 1024));
        }

        var deep = WriteSkill(Custom, "deep-skill");
        var nested = Path.Combine(deep, "a", "b", "c", "d", "e");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(nested, "x.md"), "x");

        var catalog = Registry().Reload();
        Assert.Contains("SKILL.md is larger", Single(catalog, "big-skill-file").Diagnostic!, StringComparison.Ordinal);
        Assert.Contains("more than 32 files", Single(catalog, "many-files").Diagnostic!, StringComparison.Ordinal);
        Assert.Contains("larger than 1024 KiB", Single(catalog, "heavy-skill").Diagnostic!, StringComparison.Ordinal);
        Assert.Contains("nested more than", Single(catalog, "deep-skill").Diagnostic!, StringComparison.Ordinal);
        Assert.Empty(catalog.ReadySummaries());
    }

    [Fact]
    public void Non_utf8_or_binary_content_is_invalid()
    {
        var folder = WriteSkill(Custom, "binary-skill");
        File.WriteAllBytes(Path.Combine(folder, "data.md"), [0x48, 0x00, 0x49]);

        Assert.Equal(SkillStatus.Invalid, Single(Registry().Reload(), "binary-skill").Status);
    }

    [Fact]
    public void Junction_escaping_the_skill_folder_is_rejected()
    {
        var outside = Path.Combine(_root, "outside");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "secret.md"), "secret");
        var folder = WriteSkill(Custom, "linked-skill");
        if (!TryCreateJunction(Path.Combine(folder, "refs"), outside))
        {
            return;
        }

        var registry = Registry();
        var entry = Single(registry.Reload(), "linked-skill");
        Assert.Equal(SkillStatus.Invalid, entry.Status);
        Assert.Contains("link", entry.Diagnostic!, StringComparison.Ordinal);
        Assert.False(registry.ReadForModel("linked-skill", "refs/secret.md").Ok);
    }

    [Fact]
    public void Skill_folder_that_is_a_junction_is_rejected()
    {
        var outside = WriteSkill(Path.Combine(_root, "elsewhere"), "linked-folder");
        if (!TryCreateJunction(Path.Combine(Custom, "linked-folder"), outside))
        {
            return;
        }

        var entry = Assert.Single(Registry().Reload().Entries);
        Assert.Equal(SkillStatus.Invalid, entry.Status);
        Assert.Contains("link", entry.Diagnostic!, StringComparison.Ordinal);
    }

    [Fact]
    public void Hot_reload_sees_added_edited_and_removed_skills_without_restart()
    {
        var registry = Registry();
        Assert.Empty(registry.Reload().Entries);

        var folder = WriteSkill(Custom, "hot-skill");
        Assert.Equal(SkillStatus.Ready, Single(registry.Reload(), "hot-skill").Status);

        File.WriteAllText(Path.Combine(folder, "SKILL.md"), "---\nname: hot-skill\ndescription: Changed description.\n---\n");
        Assert.Equal("Changed description.", Single(registry.Reload(), "hot-skill").Description);

        Directory.Delete(folder, recursive: true);
        Assert.Empty(registry.Reload().Entries);
        Assert.Equal(SkillReadCodes.UnknownSkill, registry.ReadForModel("hot-skill", null).Code);
    }

    [Fact]
    public void Skill_changed_after_listing_is_revalidated_before_reading()
    {
        var folder = WriteSkill(Custom, "changes-late");
        var registry = Registry();
        registry.Reload();
        File.WriteAllText(Path.Combine(folder, "evil.ps1"), "Remove-Item");

        var read = registry.ReadForModel("changes-late", null);
        Assert.Equal(SkillReadCodes.Unavailable, read.Code);
        Assert.Null(read.Content);
    }

    [Fact]
    public void External_source_add_remove_and_unavailable()
    {
        var firm = Path.Combine(_root, "firm-skills");
        WriteSkill(firm, "firm-a");
        var registry = Registry();

        var added = registry.AddExternalSource(firm);
        Assert.True(added.Ok);
        var source = added.Catalog.Sources.Single(view => view.Kind == SkillSourceKind.External);
        Assert.Equal("firm-skills", source.Label);
        Assert.Equal(SkillStatus.Ready, source.Status);
        Assert.False(registry.AddExternalSource(firm).Ok);
        Assert.False(registry.AddExternalSource(Custom).Ok);
        Assert.False(registry.AddExternalSource("relative\\path").Ok);

        WriteSkill(firm, "firm-b");
        Assert.Equal(2, registry.Reload().Entries.Count(entry => entry.Source == SkillSourceKind.External));

        Directory.Move(firm, firm + "-moved");
        var unavailable = registry.Reload();
        Assert.Equal(SkillStatus.Unavailable, unavailable.Sources.Single(view => view.Kind == SkillSourceKind.External).Status);
        Assert.DoesNotContain(unavailable.Entries, entry => entry.Source == SkillSourceKind.External);

        Assert.True(registry.RemoveExternalSource(source.Id).Ok);
        Assert.DoesNotContain(Registry().Reload().Sources, view => view.Kind == SkillSourceKind.External);
        Assert.False(registry.RemoveExternalSource(source.Id).Ok);
    }

    [Fact]
    public void Missing_built_in_folder_is_unavailable_not_fatal()
    {
        Directory.Delete(BuiltIn, recursive: true);
        WriteSkill(Custom, "still-works");

        var catalog = Registry().Reload();
        Assert.Equal(SkillStatus.Unavailable, catalog.Sources.Single(view => view.Kind == SkillSourceKind.BuiltIn).Status);
        Assert.Equal(SkillStatus.Ready, Single(catalog, "still-works").Status);
    }

    [Fact]
    public void Import_validates_then_copies_only_validated_text_into_custom()
    {
        var download = Path.Combine(_root, "downloads", "any-folder-name");
        WriteSkill(Path.GetDirectoryName(download)!, "any-folder-name", nameOverride: "imported-skill");
        Directory.CreateDirectory(Path.Combine(download, "references"));
        File.WriteAllText(Path.Combine(download, "references", "notes.md"), "notes");
        File.WriteAllText(Path.Combine(download, ".DS_Store"), "junk");
        var registry = Registry();

        var imported = registry.Import(download);

        Assert.True(imported.Ok, imported.Message);
        Assert.Equal(SkillStatus.Ready, Single(imported.Catalog, "imported-skill").Status);
        Assert.True(File.Exists(Path.Combine(Custom, "imported-skill", "references", "notes.md")));
        Assert.False(File.Exists(Path.Combine(Custom, "imported-skill", ".DS_Store")));
        Assert.Equal(Single(imported.Catalog, "imported-skill").Sha256, SkillPackageReader.Read(download, null).Package!.Sha256);
        Assert.DoesNotContain(Directory.EnumerateDirectories(Custom), directory => Path.GetFileName(directory).StartsWith(".import-", StringComparison.Ordinal));

        var again = registry.Import(download);
        Assert.False(again.Ok);
        Assert.Contains("already exists", again.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Import_rejects_executables_and_folders_without_skill_md()
    {
        var bad = WriteSkill(Path.Combine(_root, "incoming"), "with-script");
        File.WriteAllText(Path.Combine(bad, "install.ps1"), "Remove-Item C:\\");
        var registry = Registry();

        var rejected = registry.Import(bad);
        Assert.False(rejected.Ok);
        Assert.Contains("unsupported file type", rejected.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(Custom, "with-script")));

        var empty = Path.Combine(_root, "empty");
        Directory.CreateDirectory(empty);
        Assert.False(registry.Import(empty).Ok);
    }

    [Fact]
    public void Corrupt_settings_fall_back_to_defaults_without_failing()
    {
        File.WriteAllText(Settings, "{ not json");
        WriteSkill(Custom, "survives");

        var catalog = Registry().Reload();
        Assert.NotNull(catalog.Diagnostic);
        Assert.Equal(SkillStatus.Ready, Single(catalog, "survives").Status);
        Assert.Equal("{ not json", File.ReadAllText(Settings));
    }

    [Fact]
    public void Folders_without_skill_md_and_dot_folders_are_ignored()
    {
        Directory.CreateDirectory(Path.Combine(Custom, "shared-references"));
        Directory.CreateDirectory(Path.Combine(Custom, ".git"));
        WriteSkill(Custom, "real-skill");

        var entry = Assert.Single(Registry().Reload().Entries);
        Assert.Equal("real-skill", entry.Name);
    }

    [Fact]
    public void Adding_a_third_skill_folder_requires_no_code()
    {
        WriteSkill(BuiltIn, "revit-model-query");
        WriteSkill(BuiltIn, "revit-safe-write");
        WriteSkill(Custom, "electrical-bedroom-layout");

        var names = Registry().Reload().ReadySummaries().Select(summary => summary.Name).ToArray();
        Assert.Equal(["revit-model-query", "revit-safe-write", "electrical-bedroom-layout"], names);
    }

    [Fact]
    public void Shipped_built_in_skills_are_valid()
    {
        var shipped = Path.Combine(RepoRoot(), "skills", "built-in");
        var registry = new SkillRegistry(new SkillRegistryOptions(shipped, Custom, Settings));

        var catalog = registry.Reload();
        var builtIn = catalog.Entries.Where(entry => entry.Source == SkillSourceKind.BuiltIn).ToArray();
        Assert.Equal(["revit-model-query", "revit-safe-write"], builtIn.Select(entry => entry.Name).ToArray());
        Assert.All(builtIn, entry => Assert.Equal(SkillStatus.Ready, entry.Status));
        var safeWrite = registry.ReadForModel("revit-safe-write", null).Content!;
        Assert.Contains("Chat approval is not Revit approval", safeWrite, StringComparison.Ordinal);
        Assert.Contains("Never bypass preview", safeWrite, StringComparison.Ordinal);
        Assert.Contains("Never silently recalculate after approval", safeWrite, StringComparison.Ordinal);
        var query = registry.ReadForModel("revit-model-query", null).Content!;
        Assert.Contains("revit_read_spatial_rooms", query, StringComparison.Ordinal);
        Assert.DoesNotContain("get_studios_like_this_sketch", query, StringComparison.Ordinal);
    }

    [Fact]
    public void Built_in_skills_are_copied_beside_the_assistant()
    {
        var output = Path.GetDirectoryName(typeof(AssistantSkills).Assembly.Location)!;
        Assert.True(File.Exists(Path.Combine(output, "skills", "revit-model-query", "SKILL.md")));
        Assert.True(File.Exists(Path.Combine(output, "skills", "revit-safe-write", "SKILL.md")));
    }

    internal static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BimIt.Revit.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private SkillRegistry Registry() => new(new SkillRegistryOptions(BuiltIn, Custom, Settings));

    private static SkillEntry Single(SkillCatalog catalog, string name) => Assert.Single(catalog.Entries, entry => entry.Name == name);

    private static SkillFrontMatter.Parsed Parse(string text)
    {
        Assert.True(SkillFrontMatter.TryParse(text, out var parsed, out var error), error);
        return parsed!;
    }

    private static string Front(string name) => "---\nname: " + name + "\ndescription: Use when testing " + name + ".\n---\n";

    internal static string WriteSkill(string root, string folder, string? nameOverride = null, string body = "# Steps\n")
    {
        var directory = Path.Combine(root, folder);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "SKILL.md"), Front(nameOverride ?? folder) + body, new UTF8Encoding(false));
        return directory;
    }

    private static bool TryCreateJunction(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }

        using var process = Process.Start(new ProcessStartInfo("cmd.exe", "/c mklink /J \"" + link + "\" \"" + target + "\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;
        process.WaitForExit(10_000);
        return Directory.Exists(link);
    }
}
