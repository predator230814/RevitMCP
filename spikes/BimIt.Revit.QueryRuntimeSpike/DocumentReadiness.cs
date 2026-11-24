using Autodesk.Revit.DB;

namespace BimIt.Revit.QueryRuntimeSpike;

/// <summary>
/// Readiness of the active project document. Execution requires a project document that is not modifiable.
/// </summary>
public readonly record struct DocumentReadiness(bool HasDocument, bool IsFamilyDocument, bool IsModifiable)
{
    public static DocumentReadiness From(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new DocumentReadiness(true, document.IsFamilyDocument, document.IsModifiable);
    }

    public string? RejectionCode()
    {
        if (!HasDocument)
        {
            return "no_active_document";
        }

        if (IsFamilyDocument)
        {
            return "project_document_required";
        }

        if (IsModifiable)
        {
            return "document_modifiable";
        }

        return null;
    }
}
