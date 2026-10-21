using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using BimIt.Revit.Addin.Execution;
using BimIt.Revit.Addin.Identity;

namespace BimIt.Revit.Addin.Assistant;

internal enum MepDocumentCalibrationPickStatus
{
    Vector,
    Cancelled,
    Mismatch,
}

internal readonly record struct MepDocumentCalibrationPick(
    MepDocumentCalibrationPickStatus Status,
    double DeltaXMetres,
    double DeltaYMetres)
{
    public static MepDocumentCalibrationPick Vector(double deltaXMetres, double deltaYMetres)
        => new(MepDocumentCalibrationPickStatus.Vector, deltaXMetres, deltaYMetres);

    public static MepDocumentCalibrationPick Cancel() => new(MepDocumentCalibrationPickStatus.Cancelled, 0, 0);

    public static MepDocumentCalibrationPick Mismatch() => new(MepDocumentCalibrationPickStatus.Mismatch, 0, 0);
}

internal readonly record struct MepDocumentCalibrationBinding(string DocumentId, string TemplateElementRef);

internal interface IMepDocumentCalibrationPicker
{
    Task<MepDocumentCalibrationPick> PickAsync(MepDocumentCalibrationBinding binding, CancellationToken cancellationToken);
}

internal static class MepDocumentCalibrationMath
{
    public const double FeetToMetres = 0.3048;

    public static bool SameDocument(string? activeDocumentId, string expectedDocumentId)
    {
        return !string.IsNullOrEmpty(expectedDocumentId)
            && string.Equals(activeDocumentId, expectedDocumentId, StringComparison.Ordinal);
    }

    public static bool IsPointLike(double x, double y)
    {
        return double.IsFinite(x) && double.IsFinite(y);
    }

    public static bool TryDeltaMetres(
        double templateXFeet,
        double templateYFeet,
        double pickedXFeet,
        double pickedYFeet,
        out double deltaXMetres,
        out double deltaYMetres)
    {
        deltaXMetres = (pickedXFeet - templateXFeet) * FeetToMetres;
        deltaYMetres = (pickedYFeet - templateYFeet) * FeetToMetres;
        return double.IsFinite(deltaXMetres)
            && double.IsFinite(deltaYMetres)
            && (deltaXMetres != 0d || deltaYMetres != 0d);
    }
}

/// <summary>
/// Narrow Revit-local point pick. It is not an MCP tool and it does not open a transaction.
/// </summary>
internal sealed class RevitMepDocumentCalibrationPicker : IMepDocumentCalibrationPicker
{
    private const ObjectSnapTypes Snaps =
        ObjectSnapTypes.Endpoints
        | ObjectSnapTypes.Midpoints
        | ObjectSnapTypes.Nearest
        | ObjectSnapTypes.Intersections
        | ObjectSnapTypes.Centers
        | ObjectSnapTypes.Perpendicular
        | ObjectSnapTypes.Points;

    private readonly RevitExecutionDispatcher _dispatcher;
    private readonly OpenDocumentIdentityService _identity;

    public RevitMepDocumentCalibrationPicker(RevitExecutionDispatcher dispatcher, OpenDocumentIdentityService identity)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
    }

    public Task<MepDocumentCalibrationPick> PickAsync(MepDocumentCalibrationBinding binding, CancellationToken cancellationToken)
    {
        return _dispatcher.EnqueueAsync(application => Pick(application, binding), cancellationToken);
    }

    private MepDocumentCalibrationPick Pick(UIApplication application, MepDocumentCalibrationBinding binding)
    {
        try
        {
            var document = application.ActiveUIDocument?.Document;
            if (!TryPoint(document, binding, out var templateX, out var templateY))
            {
                return MepDocumentCalibrationPick.Mismatch();
            }

            XYZ picked;
            try
            {
                picked = application.ActiveUIDocument!.Selection.PickPoint(Snaps, "Pick calibration point B");
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return MepDocumentCalibrationPick.Cancel();
            }

            var after = application.ActiveUIDocument?.Document;
            if (!TryPoint(after, binding, out _, out _))
            {
                return MepDocumentCalibrationPick.Mismatch();
            }

            if (!MepDocumentCalibrationMath.IsPointLike(picked.X, picked.Y)
                || !MepDocumentCalibrationMath.TryDeltaMetres(templateX, templateY, picked.X, picked.Y, out var deltaX, out var deltaY))
            {
                return MepDocumentCalibrationPick.Mismatch();
            }

            return MepDocumentCalibrationPick.Vector(deltaX, deltaY);
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
        {
            return MepDocumentCalibrationPick.Cancel();
        }
        catch (Exception)
        {
            return MepDocumentCalibrationPick.Mismatch();
        }
    }

    private bool TryPoint(Document? document, MepDocumentCalibrationBinding binding, out double x, out double y)
    {
        x = 0;
        y = 0;
        if (document is null || !_identity.TryGet(document, out var documentId) || !MepDocumentCalibrationMath.SameDocument(documentId, binding.DocumentId))
        {
            return false;
        }

        Element? element;
        try
        {
            element = document.GetElement(binding.TemplateElementRef);
        }
        catch (Exception)
        {
            element = null;
        }

        if (element is not FamilyInstance instance
            || !string.Equals(instance.UniqueId, binding.TemplateElementRef, StringComparison.Ordinal)
            || instance.Location is not LocationPoint location
            || location.Point is not XYZ point
            || !MepDocumentCalibrationMath.IsPointLike(point.X, point.Y))
        {
            return false;
        }

        x = point.X;
        y = point.Y;
        return true;
    }
}

internal sealed class MepDocumentCalibrationPickerSlot : IMepDocumentCalibrationPicker
{
    private IMepDocumentCalibrationPicker? _inner;

    public void Attach(IMepDocumentCalibrationPicker picker)
    {
        ArgumentNullException.ThrowIfNull(picker);
        _inner = picker;
    }

    public Task<MepDocumentCalibrationPick> PickAsync(MepDocumentCalibrationBinding binding, CancellationToken cancellationToken)
    {
        var inner = _inner;
        if (inner is null)
        {
            return Task.FromResult(MepDocumentCalibrationPick.Mismatch());
        }

        return inner.PickAsync(binding, cancellationToken);
    }
}
