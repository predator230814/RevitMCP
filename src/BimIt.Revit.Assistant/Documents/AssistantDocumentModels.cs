namespace BimIt.Revit.Assistant;

public enum AssistantDocumentKind
{
    Pdf,
    Image,
    Word,
    Spreadsheet,
    Text,
    Markdown,
    Csv,
    Json,
}

public sealed class AssistantVisualPage
{
    public AssistantVisualPage(int pageNumber, string mimeType, byte[] bytes, int rasterWidth, int rasterHeight)
    {
        if (rasterWidth <= 0 || rasterHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rasterWidth), "A visual page needs a positive raster size.");
        }

        PageNumber = pageNumber;
        MimeType = mimeType;
        Bytes = bytes;
        RasterWidth = rasterWidth;
        RasterHeight = rasterHeight;
    }

    public int PageNumber { get; }

    public string MimeType { get; }

    public byte[] Bytes { get; }

    public int RasterWidth { get; }

    public int RasterHeight { get; }
}

public sealed class AssistantDocumentArtifact : IDisposable
{
    private readonly List<AssistantVisualPage> _pages;

    public AssistantDocumentArtifact(
        string displayName,
        AssistantDocumentKind kind,
        long sourceSizeBytes,
        string? textContent,
        bool textTruncated,
        IReadOnlyList<AssistantVisualPage> visualPages,
        bool visualTruncated,
        int? pageCount)
    {
        ArtifactId = Guid.NewGuid().ToString("N");
        DisplayName = displayName;
        Kind = kind;
        SourceSizeBytes = sourceSizeBytes;
        TextContent = textContent;
        TextTruncated = textTruncated;
        _pages = [.. visualPages];
        VisualTruncated = visualTruncated;
        PageCount = pageCount;
    }

    public string ArtifactId { get; }

    public string DisplayName { get; }

    public AssistantDocumentKind Kind { get; }

    public long SourceSizeBytes { get; }

    public string? TextContent { get; private set; }

    public bool TextTruncated { get; }

    public IReadOnlyList<AssistantVisualPage> VisualPages => _pages;

    public bool VisualTruncated { get; }

    public int? PageCount { get; }

    public bool IsDisposed { get; private set; }

    public void Dispose()
    {
        if (IsDisposed)
        {
            return;
        }

        IsDisposed = true;
        TextContent = null;
        _pages.Clear();
    }
}

internal static class AssistantDocumentLimits
{
    public const long MaxSourceBytes = 25L * 1024 * 1024;

    public const int MaxTextCharacters = 60_000;

    public const int MaxVisualPages = 4;

    public const int VisualLongEdge = 2_200;

    public const int MaxVisualBytes = 2 * 1024 * 1024;

    public const int MaxSourceEdge = 40_000;

    public const int MaxDecodeEdge = 4_096;

    public const long MaxDecodedBytes = 32L * 1024 * 1024;

    public const int DecodedBytesPerPixel = 4;
}

internal static class AssistantDocumentErrors
{
    public const string Unsupported = "unsupported_document";

    public const string TooLarge = "document_too_large";

    public const string Invalid = "document_invalid";

    public const string ReadFailed = "document_read_failed";

    public const string PdfRenderFailed = "pdf_render_failed";

    public const string UnsupportedMessage = "This file type is not supported.";

    public const string TooLargeMessage = "The document is larger than 25 MB.";

    public const string InvalidMessage = "The document could not be read as a supported file.";

    public const string ReadFailedMessage = "The document could not be read.";

    public const string PdfRenderFailedMessage = "The PDF pages could not be rendered.";
}

internal sealed record AssistantDocumentReadResult(AssistantDocumentArtifact? Artifact, string? Code, string? Message)
{
    public static AssistantDocumentReadResult Success(AssistantDocumentArtifact artifact) => new(artifact, null, null);

    public static AssistantDocumentReadResult Failure(string code, string message) => new(null, code, message);
}

internal static class AssistantDocumentKinds
{
    public static bool TryFromExtension(string? extension, out AssistantDocumentKind kind)
    {
        switch (extension?.ToLowerInvariant())
        {
            case ".pdf":
                kind = AssistantDocumentKind.Pdf;
                return true;
            case ".png":
            case ".jpg":
            case ".jpeg":
                kind = AssistantDocumentKind.Image;
                return true;
            case ".docx":
                kind = AssistantDocumentKind.Word;
                return true;
            case ".xlsx":
                kind = AssistantDocumentKind.Spreadsheet;
                return true;
            case ".txt":
                kind = AssistantDocumentKind.Text;
                return true;
            case ".md":
                kind = AssistantDocumentKind.Markdown;
                return true;
            case ".csv":
                kind = AssistantDocumentKind.Csv;
                return true;
            case ".json":
                kind = AssistantDocumentKind.Json;
                return true;
            default:
                kind = default;
                return false;
        }
    }

    public static string Wire(AssistantDocumentKind kind)
    {
        return kind switch
        {
            AssistantDocumentKind.Pdf => "pdf",
            AssistantDocumentKind.Image => "image",
            AssistantDocumentKind.Word => "word",
            AssistantDocumentKind.Spreadsheet => "spreadsheet",
            AssistantDocumentKind.Text => "text",
            AssistantDocumentKind.Markdown => "markdown",
            AssistantDocumentKind.Csv => "csv",
            AssistantDocumentKind.Json => "json",
            _ => "text",
        };
    }
}

internal static class AssistantDocumentPaths
{
    public static bool TryCanonicalize(string? path, out string fullPath, out AssistantDocumentReadResult? failure)
    {
        fullPath = "";
        failure = null;
        if (string.IsNullOrWhiteSpace(path) || path.Contains("://", StringComparison.Ordinal) || !Path.IsPathRooted(path))
        {
            failure = AssistantDocumentReadResult.Failure(AssistantDocumentErrors.Invalid, AssistantDocumentErrors.InvalidMessage);
            return false;
        }

        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception)
        {
            failure = AssistantDocumentReadResult.Failure(AssistantDocumentErrors.Invalid, AssistantDocumentErrors.InvalidMessage);
            return false;
        }

        var root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrEmpty(root))
        {
            failure = AssistantDocumentReadResult.Failure(AssistantDocumentErrors.Invalid, AssistantDocumentErrors.InvalidMessage);
            return false;
        }

        var remainder = fullPath[root.Length..];
        if (remainder.Contains(':') || remainder.Contains('\0'))
        {
            failure = AssistantDocumentReadResult.Failure(AssistantDocumentErrors.Invalid, AssistantDocumentErrors.InvalidMessage);
            return false;
        }

        return true;
    }

    public static string DisplayName(string fullPath)
    {
        var name = Path.GetFileName(fullPath).Replace('\\', ' ').Replace('/', ' ').Replace('\0', ' ').Trim();
        if (name.Length > 180)
        {
            name = name[..180];
        }

        return string.IsNullOrWhiteSpace(name) ? "document" : name;
    }
}

internal sealed class AssistantDocumentStore
{
    public AssistantDocumentArtifact? Current { get; private set; }

    public AssistantDocumentReadResult Attach(string? path)
    {
        AssistantDocumentReadResult read;
        try
        {
            read = AssistantDocumentReader.Read(path);
        }
        catch (Exception)
        {
            return AssistantDocumentReadResult.Failure(AssistantDocumentErrors.ReadFailed, AssistantDocumentErrors.ReadFailedMessage);
        }

        if (read.Artifact is null)
        {
            return read;
        }

        var previous = Current;
        Current = read.Artifact;
        if (!ReferenceEquals(previous, Current))
        {
            previous?.Dispose();
        }

        return read;
    }

    public void Clear()
    {
        var previous = Current;
        Current = null;
        previous?.Dispose();
    }
}
