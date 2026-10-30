using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace BimIt.Revit.Skills;

public sealed record SkillPackageRead(SkillPackage? Package, string? Name, string? Diagnostic)
{
    public bool Valid => Package is not null;
}

/// <summary>
/// Validates one Skill folder and reads it into memory. The folder is guidance only: files are read as UTF-8 text
/// and never executed. Every bound fails closed with a short diagnostic that contains no filesystem path.
/// </summary>
public static partial class SkillPackageReader
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static bool IsValidName(string? name)
    {
        return name is not null && name.Length <= SkillLimits.MaxNameLength && NamePattern().IsMatch(name);
    }

    /// <param name="directory">The Skill folder.</param>
    /// <param name="expectedName">The folder name the <c>name</c> field must match, or null when importing.</param>
    public static SkillPackageRead Read(string directory, string? expectedName)
    {
        DirectoryInfo root;
        try
        {
            root = new DirectoryInfo(Path.GetFullPath(directory));
            if (!root.Exists)
            {
                return Invalid("The Skill folder could not be read.");
            }

            if (IsLink(root))
            {
                return Invalid("The Skill folder is a link. Links are not followed.");
            }
        }
        catch (Exception exception) when (IsFileSystemFailure(exception))
        {
            return Invalid("The Skill folder could not be read.");
        }

        var files = new List<(string Relative, FileInfo Info)>();
        var pending = new Stack<(DirectoryInfo Directory, int Depth)>();
        pending.Push((root, 0));
        var scanned = 0;
        long total = 0;
        var rootPrefix = root.FullName.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        try
        {
            while (pending.Count > 0)
            {
                var (current, depth) = pending.Pop();
                foreach (var entry in current.EnumerateFileSystemInfos("*", new EnumerationOptions
                {
                    RecurseSubdirectories = false,
                    IgnoreInaccessible = false,
                    AttributesToSkip = 0,
                    ReturnSpecialDirectories = false,
                }))
                {
                    if (++scanned > SkillLimits.MaxEntriesScanned)
                    {
                        return Invalid("The Skill folder has too many entries.");
                    }

                    if (entry.Name.StartsWith('.'))
                    {
                        // Hidden dot entries such as .DS_Store are not part of a Skill. They are never read or copied.
                        continue;
                    }

                    if (IsLink(entry))
                    {
                        return Invalid("The Skill folder contains a link. Links are not followed.");
                    }

                    if (!entry.FullName.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        return Invalid("The Skill folder contains an entry outside the folder.");
                    }

                    if (entry is DirectoryInfo child)
                    {
                        if (depth + 1 > SkillLimits.MaxDepth)
                        {
                            return Invalid("The Skill folder is nested more than " + SkillLimits.MaxDepth + " levels deep.");
                        }

                        pending.Push((child, depth + 1));
                        continue;
                    }

                    var file = (FileInfo)entry;
                    var relative = entry.FullName[rootPrefix.Length..].Replace(Path.DirectorySeparatorChar, '/');
                    if (relative.Length > SkillLimits.MaxRelativePathLength)
                    {
                        return Invalid("A Skill file path is too long.");
                    }

                    if (!SkillLimits.IsAllowedExtension(file.Name))
                    {
                        return Invalid("The Skill contains an unsupported file type (" + SafeExtension(file.Name) + "). Allowed: "
                            + string.Join(", ", SkillLimits.AllowedExtensions) + ".");
                    }

                    if (files.Count + 1 > SkillLimits.MaxFiles)
                    {
                        return Invalid("The Skill has more than " + SkillLimits.MaxFiles + " files.");
                    }

                    total += file.Length;
                    if (total > SkillLimits.MaxSkillBytes)
                    {
                        return Invalid("The Skill is larger than " + SkillLimits.MaxSkillBytes / 1024 + " KiB.");
                    }

                    files.Add((relative, file));
                }
            }
        }
        catch (Exception exception) when (IsFileSystemFailure(exception))
        {
            return Invalid("The Skill folder could not be read.");
        }

        var skillFile = files.FirstOrDefault(file => file.Relative == SkillLimits.SkillFileName);
        if (skillFile.Info is null)
        {
            return Invalid("The Skill folder has no SKILL.md.");
        }

        if (skillFile.Info.Length > SkillLimits.MaxSkillFileBytes)
        {
            return Invalid("SKILL.md is larger than " + SkillLimits.MaxSkillFileBytes / 1024 + " KiB.");
        }

        var duplicate = files
            .GroupBy(file => file.Relative, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            return Invalid("The Skill has two files whose names differ only by case.");
        }

        var loaded = new List<SkillFile>(files.Count);
        total = 0;
        foreach (var (relative, info) in files.OrderBy(file => file.Relative, StringComparer.Ordinal))
        {
            var limit = relative == SkillLimits.SkillFileName ? SkillLimits.MaxSkillFileBytes : SkillLimits.MaxSkillBytes;
            byte[] bytes;
            try
            {
                bytes = ReadBounded(info.FullName, limit);
            }
            catch (InvalidDataException)
            {
                return Invalid("A Skill file grew past its bound while it was read.");
            }
            catch (Exception exception) when (IsFileSystemFailure(exception))
            {
                return Invalid("A Skill file could not be read.");
            }

            total += bytes.Length;
            if (total > SkillLimits.MaxSkillBytes)
            {
                return Invalid("The Skill is larger than " + SkillLimits.MaxSkillBytes / 1024 + " KiB.");
            }

            if (!TryDecode(bytes, out var text))
            {
                return Invalid("A Skill file is not UTF-8 text.");
            }

            loaded.Add(new SkillFile(relative, bytes, text));
        }

        var markdown = loaded.First(file => file.RelativePath == SkillLimits.SkillFileName).Text;
        if (!SkillFrontMatter.TryParse(markdown, out var parsed, out var error))
        {
            return Invalid(error);
        }

        parsed!.Values.TryGetValue("name", out var name);
        parsed.Values.TryGetValue("description", out var description);
        name = name?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return Invalid("The front matter has no name.");
        }

        if (!IsValidName(name))
        {
            return Invalid("The name must be 1 to 64 lowercase letters, digits, or single hyphens, and must not start or end with a hyphen.");
        }

        if (expectedName is not null && !string.Equals(name, expectedName, StringComparison.Ordinal))
        {
            return new SkillPackageRead(null, name, "The name '" + name + "' does not match the Skill folder name.");
        }

        description = Clean(description);
        if (string.IsNullOrEmpty(description))
        {
            return new SkillPackageRead(null, name, "The front matter has no description.");
        }

        if (description.Length > SkillLimits.MaxDescriptionLength)
        {
            return new SkillPackageRead(null, name, "The description is longer than " + SkillLimits.MaxDescriptionLength + " characters.");
        }

        return new SkillPackageRead(new SkillPackage(name, description, loaded, Hash(loaded)), name, null);
    }

    /// <summary>
    /// SHA-256 over every validated file in ordinal path order. Each file contributes its UTF-8 relative path, a zero
    /// byte, its 8-byte length, and its bytes. Changing, adding, removing, or renaming a file changes the hash.
    /// </summary>
    public static string Hash(IEnumerable<SkillFile> files)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> length = stackalloc byte[8];
        foreach (var file in files.OrderBy(file => file.RelativePath, StringComparer.Ordinal))
        {
            hash.AppendData(Encoding.UTF8.GetBytes(file.RelativePath));
            hash.AppendData([0]);
            BinaryPrimitives.WriteInt64BigEndian(length, file.Bytes.LongLength);
            hash.AppendData(length);
            hash.AppendData(file.Bytes);
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    /// <summary>
    /// A symbolic link or junction. Cloud placeholders (OneDrive, SharePoint sync) are reparse points without a link
    /// target. They read like ordinary files and stay inside the folder, so they are allowed.
    /// </summary>
    internal static bool IsLink(FileSystemInfo entry)
    {
        if ((entry.Attributes & FileAttributes.ReparsePoint) == 0)
        {
            return false;
        }

        try
        {
            return entry.LinkTarget is not null;
        }
        catch (Exception exception) when (IsFileSystemFailure(exception))
        {
            return true;
        }
    }

    internal static bool IsFileSystemFailure(Exception exception)
    {
        return exception is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException or NotSupportedException;
    }

    /// <summary>One line of text: control characters become spaces and runs of whitespace collapse.</summary>
    public static string Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "";
        }

        var builder = new StringBuilder(text.Length);
        foreach (var character in text.Trim())
        {
            builder.Append(char.IsControl(character) ? ' ' : character);
        }

        return MultipleSpaces().Replace(builder.ToString(), " ").Trim();
    }

    private static byte[] ReadBounded(string path, long limit)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
        {
            if (buffer.Length + read > limit)
            {
                throw new InvalidDataException();
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static bool TryDecode(byte[] bytes, out string text)
    {
        text = "";
        try
        {
            var span = bytes.AsSpan();
            if (span.Length >= 3 && span[0] == 0xEF && span[1] == 0xBB && span[2] == 0xBF)
            {
                span = span[3..];
            }

            text = StrictUtf8.GetString(span);
            return !text.Contains('\0');
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    private static string SafeExtension(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        if (string.IsNullOrEmpty(extension))
        {
            return "no extension";
        }

        return extension.Length > 12 || extension.Any(character => !char.IsLetterOrDigit(character) && character != '.')
            ? "unsupported extension"
            : extension.ToLowerInvariant();
    }

    private static SkillPackageRead Invalid(string? diagnostic) => new(null, null, diagnostic ?? "The Skill is invalid.");

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex NamePattern();

    [GeneratedRegex("\\s{2,}")]
    private static partial Regex MultipleSpaces();
}
