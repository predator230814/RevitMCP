using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;

namespace BimIt.Revit.Addin.Identity;

/// <summary>
/// Signals that a host document may have changed in a way that can affect FamilySymbol
/// inventory or placed-instance counts. The revision service advances on every signal.
/// These subscriptions do not prove that Revit raises each event for every such change.
/// </summary>
internal interface IDocumentQueryChangeEventSource
{
    IDisposable Subscribe(Action<Document> onPossibleInventoryChange);
}

/// <summary>
/// Registers the Revit handlers in the constructor, which must run inside
/// <c>IExternalApplication.OnStartup</c>. In live Revit 2026.5, a <c>DocumentChanged</c>
/// handler added to the stored startup <see cref="ControlledApplication"/> later, from the
/// Idling bootstrap, received no event. <see cref="Subscribe"/> only attaches a listener to the relay.
/// The Revit handlers stay registered for the add-in process lifetime.
/// </summary>
internal sealed class RevitDocumentQueryChangeEventSource : IDocumentQueryChangeEventSource
{
    private readonly DocumentQueryChangeRelay<Document> _relay = new();

    public RevitDocumentQueryChangeEventSource(ControlledApplication application)
    {
        ArgumentNullException.ThrowIfNull(application);
        application.DocumentChanged += (_, args) => _relay.Publish(args.GetDocument());
        application.DocumentSynchronizedWithCentral += (_, args) => _relay.Publish(args.Document);
        application.DocumentReloadedLatest += (_, args) => _relay.Publish(args.Document);
    }

    public IDisposable Subscribe(Action<Document> onPossibleInventoryChange)
    {
        return _relay.Subscribe(onPossibleInventoryChange);
    }
}
