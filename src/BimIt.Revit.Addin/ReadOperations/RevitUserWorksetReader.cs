using Autodesk.Revit.DB;

namespace BimIt.Revit.Addin.ReadOperations;

internal sealed class RevitUserWorksetReader
{
    public UserWorksetInventory Read(Document document)
    {
        if (!document.IsWorkshared)
        {
            return new UserWorksetInventory(false, []);
        }

        var collector = new FilteredWorksetCollector(document);
        var rows = new List<UserWorksetCandidate>();
        foreach (Workset workset in collector.OfKind(WorksetKind.UserWorkset))
        {
            rows.Add(new UserWorksetCandidate(
                workset.Name ?? string.Empty,
                workset.IsDefaultWorkset,
                workset.IsVisibleByDefault));
        }

        return new UserWorksetInventory(true, rows);
    }
}
