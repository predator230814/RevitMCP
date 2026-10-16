using System.Globalization;
using GitHub.Copilot;

namespace BimIt.Revit.Assistant.Providers.GitHubCopilot;

internal static class CopilotTurnComposer
{
    public static MessageOptions Compose(string userText, AssistantDocumentArtifact? attachment)
    {
        if (attachment is null || attachment.IsDisposed)
        {
            return new MessageOptions { Prompt = userText };
        }

        List<Attachment>? attachments = null;
        if (attachment.VisualPages.Count > 0)
        {
            attachments = new List<Attachment>(attachment.VisualPages.Count);
            foreach (var page in attachment.VisualPages)
            {
                if (page.MimeType is not ("image/png" or "image/jpeg") || page.Bytes.Length == 0 || page.Bytes.Length > AssistantDocumentLimits.MaxVisualBytes)
                {
                    throw new InvalidOperationException("The attached page could not be prepared.");
                }

                attachments.Add(new AttachmentBlob
                {
                    Data = Convert.ToBase64String(page.Bytes),
                    MimeType = page.MimeType,
                    DisplayName = PageName(attachment.DisplayName, page.PageNumber),
                });
            }
        }

        return new MessageOptions
        {
            Prompt = AssistantDocumentPrompt.Compose(userText, attachment),
            Attachments = attachments,
        };
    }

    public static string PageName(string displayName, int pageNumber)
    {
        return displayName + " - page " + pageNumber.ToString(CultureInfo.InvariantCulture);
    }
}

internal sealed class CopilotAttachmentDelivery
{
    private string? _artifactId;

    public bool ShouldDeliver(AssistantDocumentArtifact? attachment)
    {
        return attachment is not null
            && !attachment.IsDisposed
            && !string.Equals(_artifactId, attachment.ArtifactId, StringComparison.Ordinal);
    }

    public void MarkDelivered(string artifactId)
    {
        _artifactId = artifactId;
    }

    public void Reset()
    {
        _artifactId = null;
    }
}
