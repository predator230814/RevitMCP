using Microsoft.Win32;

namespace BimIt.Revit.Addin.Assistant;

internal interface IAssistantDocumentPicker
{
    string? TryPickSupportedDocument();
}

internal sealed class AssistantOpenFilePicker : IAssistantDocumentPicker
{
    public const string Filter =
        "Supported documents|*.pdf;*.png;*.jpg;*.jpeg;*.docx;*.xlsx;*.txt;*.md;*.csv;*.json|PDF (*.pdf)|*.pdf|Images (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg|Word (*.docx)|*.docx|Excel (*.xlsx)|*.xlsx|Text (*.txt;*.md;*.csv;*.json)|*.txt;*.md;*.csv;*.json";

    public string? TryPickSupportedDocument()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Attach a document",
            Filter = Filter,
            Multiselect = false,
            CheckFileExists = true,
            CheckPathExists = true,
            DereferenceLinks = true,
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}

internal static class AssistantAttachmentSummary
{
    public static string Describe(string? kind, int? pageCount, int? visualCount, bool truncated)
    {
        if (kind is "pdf")
        {
            var shown = visualCount ?? 0;
            var total = pageCount ?? shown;
            if (truncated || total > shown)
            {
                return "PDF · " + shown + " of " + total + " pages";
            }

            return shown == 1 ? "PDF · 1 page" : "PDF · " + shown + " pages";
        }

        if (kind is "image")
        {
            return "Image · 1 page";
        }

        var label = kind switch
        {
            "word" => "Word",
            "spreadsheet" => "Excel",
            "text" => "Text",
            "markdown" => "Markdown",
            "csv" => "CSV",
            "json" => "JSON",
            _ => "Document",
        };
        return truncated ? label + " · text extracted, truncated" : label + " · text extracted";
    }
}
