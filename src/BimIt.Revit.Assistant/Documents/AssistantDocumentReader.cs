namespace BimIt.Revit.Assistant;

internal static class AssistantDocumentReader
{
    public static AssistantDocumentReadResult Read(string? path)
    {
        if (!AssistantDocumentPaths.TryCanonicalize(path, out var fullPath, out var failure))
        {
            return failure!;
        }

        if (!AssistantDocumentKinds.TryFromExtension(Path.GetExtension(fullPath), out var kind))
        {
            return AssistantDocumentReadResult.Failure(AssistantDocumentErrors.Unsupported, AssistantDocumentErrors.UnsupportedMessage);
        }

        FileInfo info;
        try
        {
            info = new FileInfo(fullPath);
            if (!info.Exists
                || info.Attributes.HasFlag(FileAttributes.Directory)
                || info.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                return AssistantDocumentReadResult.Failure(AssistantDocumentErrors.Invalid, AssistantDocumentErrors.InvalidMessage);
            }

            if (info.Length > AssistantDocumentLimits.MaxSourceBytes)
            {
                return AssistantDocumentReadResult.Failure(AssistantDocumentErrors.TooLarge, AssistantDocumentErrors.TooLargeMessage);
            }
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return AssistantDocumentReadResult.Failure(AssistantDocumentErrors.Invalid, AssistantDocumentErrors.InvalidMessage);
        }
        catch (Exception)
        {
            return AssistantDocumentReadResult.Failure(AssistantDocumentErrors.ReadFailed, AssistantDocumentErrors.ReadFailedMessage);
        }

        var displayName = AssistantDocumentPaths.DisplayName(fullPath);
        try
        {
            return kind switch
            {
                AssistantDocumentKind.Pdf => AssistantVisualReader.ReadPdf(fullPath, displayName, info.Length),
                AssistantDocumentKind.Image => AssistantVisualReader.ReadImage(fullPath, displayName, info.Length),
                AssistantDocumentKind.Word => AssistantOpenXmlReader.ReadWord(fullPath, displayName, info.Length),
                AssistantDocumentKind.Spreadsheet => AssistantOpenXmlReader.ReadWorkbook(fullPath, displayName, info.Length),
                AssistantDocumentKind.Json => AssistantTextReader.ReadJson(fullPath, displayName, info.Length),
                _ => AssistantTextReader.ReadPlain(fullPath, displayName, info.Length, kind),
            };
        }
        catch (Exception)
        {
            var code = kind == AssistantDocumentKind.Pdf ? AssistantDocumentErrors.PdfRenderFailed : AssistantDocumentErrors.ReadFailed;
            var message = kind == AssistantDocumentKind.Pdf ? AssistantDocumentErrors.PdfRenderFailedMessage : AssistantDocumentErrors.ReadFailedMessage;
            return AssistantDocumentReadResult.Failure(code, message);
        }
    }

    internal static FileStream OpenRead(string path)
    {
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
    }
}
