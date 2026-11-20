namespace BimIt.Revit.Addin.Approval;

/// <summary>
/// One Addin-owned place that can show the current review.
/// Publishing and clearing are presentation. They do not record a decision.
/// </summary>
internal interface IApprovalPresentationTarget
{
    void Publish(string json);

    void Clear();
}
