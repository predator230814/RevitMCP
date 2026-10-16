using System.Globalization;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Word = DocumentFormat.OpenXml.Wordprocessing;

namespace BimIt.Revit.Assistant;

internal static class AssistantOpenXmlReader
{
    public static AssistantDocumentReadResult ReadWord(string path, string displayName, long size)
    {
        try
        {
            using var stream = AssistantDocumentReader.OpenRead(path);
            using var document = WordprocessingDocument.Open(stream, false);
            var body = document.MainDocumentPart?.Document?.Body;
            if (body is null)
            {
                return Invalid();
            }

            var builder = new StringBuilder();
            var truncated = false;
            WalkBlocks(body, builder, ref truncated);
            return TextArtifact(displayName, AssistantDocumentKind.Word, size, builder.ToString(), truncated);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ReadFailed();
        }
        catch (Exception)
        {
            return Invalid();
        }
    }

    public static AssistantDocumentReadResult ReadWorkbook(string path, string displayName, long size)
    {
        try
        {
            using var stream = AssistantDocumentReader.OpenRead(path);
            using var document = SpreadsheetDocument.Open(stream, false);
            var workbookPart = document.WorkbookPart;
            var sheets = workbookPart?.Workbook?.Sheets;
            if (workbookPart is null || sheets is null)
            {
                return Invalid();
            }

            var shared = workbookPart.SharedStringTablePart?.SharedStringTable;
            var builder = new StringBuilder();
            var truncated = false;
            foreach (var sheet in sheets.OfType<Sheet>())
            {
                if (truncated)
                {
                    break;
                }

                var sheetName = string.IsNullOrWhiteSpace(sheet.Name?.Value) ? "Sheet" : sheet.Name.Value;
                Append(builder, "Sheet: " + sheetName, ref truncated);
                if (truncated || sheet.Id?.Value is null)
                {
                    continue;
                }

                if (workbookPart.GetPartById(sheet.Id.Value) is not WorksheetPart worksheet)
                {
                    continue;
                }

                var cells = (worksheet.Worksheet?.Descendants<Cell>() ?? [])
                    .Select((cell, index) => (cell, index))
                    .OrderBy(item => CellOrder(item.cell.CellReference?.Value))
                    .ThenBy(item => item.index);
                foreach (var (cell, _) in cells)
                {
                    var line = FormatCell(cell, shared);
                    if (line is null)
                    {
                        continue;
                    }

                    if (!Append(builder, line, ref truncated))
                    {
                        break;
                    }
                }
            }

            return TextArtifact(displayName, AssistantDocumentKind.Spreadsheet, size, builder.ToString(), truncated);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ReadFailed();
        }
        catch (Exception)
        {
            return Invalid();
        }
    }

    private static void WalkBlocks(OpenXmlElement parent, StringBuilder builder, ref bool truncated)
    {
        foreach (var child in parent.ChildElements)
        {
            if (truncated)
            {
                return;
            }

            switch (child)
            {
                case Word.Paragraph paragraph:
                    Append(builder, ParagraphText(paragraph), ref truncated);
                    break;
                case Word.Table table:
                    AppendTable(table, builder, ref truncated);
                    break;
                default:
                    if (child.LocalName == "sdt")
                    {
                        foreach (var content in child.ChildElements.Where(element => element.LocalName == "sdtContent"))
                        {
                            WalkBlocks(content, builder, ref truncated);
                        }
                    }

                    break;
            }
        }
    }

    private static void AppendTable(Word.Table table, StringBuilder builder, ref bool truncated)
    {
        foreach (var row in table.Elements<Word.TableRow>())
        {
            if (truncated)
            {
                return;
            }

            var cells = new List<string>();
            foreach (var cell in row.Elements<Word.TableCell>())
            {
                var cellBuilder = new StringBuilder();
                foreach (var paragraph in cell.Elements<Word.Paragraph>())
                {
                    if (cellBuilder.Length > 0)
                    {
                        cellBuilder.Append(' ');
                    }

                    cellBuilder.Append(ParagraphText(paragraph));
                }

                cells.Add(cellBuilder.ToString());
            }

            Append(builder, string.Join(" | ", cells), ref truncated);
        }
    }

    private static string ParagraphText(Word.Paragraph paragraph)
    {
        var builder = new StringBuilder();
        AppendInline(paragraph, builder);
        return builder.ToString();
    }

    private static void AppendInline(OpenXmlElement element, StringBuilder builder)
    {
        foreach (var child in element.ChildElements)
        {
            if (child is Word.Drawing || child.LocalName is "pict" or "object")
            {
                continue;
            }

            switch (child)
            {
                case Word.Text text:
                    builder.Append(text.Text);
                    break;
                case Word.Break:
                    builder.Append('\n');
                    break;
                case Word.TabChar:
                    builder.Append('\t');
                    break;
                default:
                    AppendInline(child, builder);
                    break;
            }
        }
    }

    private static string? FormatCell(Cell cell, SharedStringTable? shared)
    {
        var reference = cell.CellReference?.Value;
        if (string.IsNullOrWhiteSpace(reference))
        {
            return null;
        }

        var cached = CachedValue(cell, shared);
        var formula = cell.CellFormula?.Text;
        if (string.IsNullOrWhiteSpace(formula) && string.IsNullOrWhiteSpace(cached))
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(formula))
        {
            var formulaText = formula.Trim();
            if (!formulaText.StartsWith('='))
            {
                formulaText = "=" + formulaText;
            }

            return string.IsNullOrWhiteSpace(cached)
                ? reference + ": " + formulaText
                : reference + ": " + formulaText + " | " + cached;
        }

        return reference + ": " + cached;
    }

    private static string? CachedValue(Cell cell, SharedStringTable? shared)
    {
        if (cell.DataType?.Value == CellValues.SharedString
            && int.TryParse(cell.CellValue?.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
        {
            var item = shared?.Elements<SharedStringItem>().ElementAtOrDefault(index);
            var text = item?.InnerText;
            return string.IsNullOrEmpty(text) ? null : text;
        }

        if (cell.DataType?.Value == CellValues.InlineString)
        {
            var inline = cell.InlineString?.InnerText;
            return string.IsNullOrEmpty(inline) ? null : inline;
        }

        var value = cell.CellValue?.Text;
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static (int Row, int Column) CellOrder(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return (int.MaxValue, int.MaxValue);
        }

        var column = 0;
        var index = 0;
        while (index < reference.Length && char.IsLetter(reference[index]))
        {
            column = (column * 26) + (char.ToUpperInvariant(reference[index]) - 'A' + 1);
            index++;
        }

        if (index == 0 || !int.TryParse(reference.AsSpan(index), NumberStyles.Integer, CultureInfo.InvariantCulture, out var row))
        {
            return (int.MaxValue, int.MaxValue);
        }

        return (row, column);
    }

    private static bool Append(StringBuilder builder, string text, ref bool truncated)
    {
        if (truncated || string.IsNullOrEmpty(text))
        {
            return !truncated;
        }

        var line = builder.Length == 0 ? text : "\n" + text;
        var room = AssistantDocumentLimits.MaxTextCharacters - builder.Length;
        if (line.Length <= room)
        {
            builder.Append(line);
            return true;
        }

        if (room > 0)
        {
            builder.Append(line.AsSpan(0, room));
        }

        truncated = true;
        return false;
    }

    private static AssistantDocumentReadResult TextArtifact(
        string displayName,
        AssistantDocumentKind kind,
        long size,
        string text,
        bool truncated)
    {
        return AssistantDocumentReadResult.Success(new AssistantDocumentArtifact(
            displayName,
            kind,
            size,
            text,
            truncated,
            [],
            false,
            null));
    }

    private static AssistantDocumentReadResult Invalid()
    {
        return AssistantDocumentReadResult.Failure(AssistantDocumentErrors.Invalid, AssistantDocumentErrors.InvalidMessage);
    }

    private static AssistantDocumentReadResult ReadFailed()
    {
        return AssistantDocumentReadResult.Failure(AssistantDocumentErrors.ReadFailed, AssistantDocumentErrors.ReadFailedMessage);
    }
}
