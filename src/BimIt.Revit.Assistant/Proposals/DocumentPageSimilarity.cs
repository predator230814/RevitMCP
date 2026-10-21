namespace BimIt.Revit.Assistant;

internal readonly record struct PagePixel(double X, double Y);

internal readonly record struct Similarity2D(double AnchorX, double AnchorY, double Real, double Imag)
{
    public double ScaleSquared => (Real * Real) + (Imag * Imag);

    public bool TryApply(double pixelX, double pixelY, out double metresX, out double metresY)
    {
        var dx = pixelX - AnchorX;
        var dy = pixelY - AnchorY;
        metresX = (Real * dx) - (Imag * dy);
        metresY = (Imag * dx) + (Real * dy);
        return double.IsFinite(metresX) && double.IsFinite(metresY);
    }
}

/// <summary>
/// Orientation-preserving 2D similarity from a drawing page onto Revit project axes.
/// Page Y is converted to a bottom-left, upward axis before the similarity is built.
/// </summary>
internal static class DocumentPageSimilarity
{
    public const double AbsoluteMinimumSeparationPixels = 8d;

    public const double LongEdgeSeparationFraction = 0.01d;

    public static double MinimumSeparation(int rasterWidth, int rasterHeight)
    {
        var longEdge = Math.Max(rasterWidth, rasterHeight);
        return Math.Max(AbsoluteMinimumSeparationPixels, longEdge * LongEdgeSeparationFraction);
    }

    public static bool TryPagePixel(double pageX, double pageY, int rasterWidth, int rasterHeight, out PagePixel pixel)
    {
        pixel = default;
        if (!IsUnitInterval(pageX)
            || !IsUnitInterval(pageY)
            || rasterWidth <= 0
            || rasterHeight <= 0)
        {
            return false;
        }

        var x = pageX * rasterWidth;
        var y = (1d - pageY) * rasterHeight;
        if (!double.IsFinite(x) || !double.IsFinite(y))
        {
            return false;
        }

        pixel = new PagePixel(x, y);
        return true;
    }

    public static bool TryCreate(
        PagePixel sourceA,
        PagePixel sourceB,
        int rasterWidth,
        int rasterHeight,
        double targetDeltaXMetres,
        double targetDeltaYMetres,
        out Similarity2D similarity)
    {
        similarity = default;
        if (rasterWidth <= 0
            || rasterHeight <= 0
            || !double.IsFinite(sourceA.X)
            || !double.IsFinite(sourceA.Y)
            || !double.IsFinite(sourceB.X)
            || !double.IsFinite(sourceB.Y)
            || !double.IsFinite(targetDeltaXMetres)
            || !double.IsFinite(targetDeltaYMetres))
        {
            return false;
        }

        var vx = sourceB.X - sourceA.X;
        var vy = sourceB.Y - sourceA.Y;
        var v2 = (vx * vx) + (vy * vy);
        var minimum = MinimumSeparation(rasterWidth, rasterHeight);
        if (!double.IsFinite(vx)
            || !double.IsFinite(vy)
            || !double.IsFinite(v2)
            || !double.IsFinite(minimum)
            || v2 < minimum * minimum)
        {
            return false;
        }

        var u2 = (targetDeltaXMetres * targetDeltaXMetres) + (targetDeltaYMetres * targetDeltaYMetres);
        if (!double.IsFinite(u2) || u2 == 0d)
        {
            return false;
        }

        var real = ((targetDeltaXMetres * vx) + (targetDeltaYMetres * vy)) / v2;
        var imag = ((targetDeltaYMetres * vx) - (targetDeltaXMetres * vy)) / v2;
        if (!double.IsFinite(real) || !double.IsFinite(imag))
        {
            return false;
        }

        var scaleSquared = (real * real) + (imag * imag);
        if (!double.IsFinite(scaleSquared) || scaleSquared <= 0d)
        {
            return false;
        }

        similarity = new Similarity2D(sourceA.X, sourceA.Y, real, imag);
        return true;
    }

    public static bool IsUnitInterval(double value)
    {
        return double.IsFinite(value) && value >= 0d && value <= 1d;
    }
}

internal static class DocumentPagePlacementTransform
{
    public static bool TryTransform(
        Similarity2D similarity,
        int rasterWidth,
        int rasterHeight,
        string lengthUnit,
        IReadOnlyList<MepDevicePlacementProposalItem> placements,
        out List<MepDevicePlacementProposalItem> transformed,
        out string failure)
    {
        transformed = [];
        failure = MepDevicePlacementProposalFailures.NonFinite;
        if (similarity.ScaleSquared <= 0d || !double.IsFinite(similarity.ScaleSquared))
        {
            return false;
        }

        var seen = new HashSet<OffsetTriple>();
        foreach (var item in placements)
        {
            if (item.PageX is not double pageX || item.PageY is not double pageY)
            {
                failure = MepDevicePlacementProposalFailures.Shape;
                transformed = [];
                return false;
            }

            if (!DocumentPageSimilarity.TryPagePixel(pageX, pageY, rasterWidth, rasterHeight, out var pixel)
                || !similarity.TryApply(pixel.X, pixel.Y, out var metresX, out var metresY)
                || !MepDevicePlacementLengthUnits.TryFromMetres(lengthUnit, metresX, out var offsetX)
                || !MepDevicePlacementLengthUnits.TryFromMetres(lengthUnit, metresY, out var offsetY)
                || !MepDevicePlacementLengthUnits.TryToMetres(lengthUnit, item.VerticalOffset, out var verticalMetres))
            {
                failure = MepDevicePlacementProposalFailures.NonFinite;
                transformed = [];
                return false;
            }

            if (Math.Abs(metresX) > MepDevicePlacementProposalTool.MaxPlanMetres + 1e-9
                || Math.Abs(metresY) > MepDevicePlacementProposalTool.MaxPlanMetres + 1e-9
                || Math.Abs(verticalMetres) > MepDevicePlacementProposalTool.MaxVerticalMetres + 1e-9)
            {
                failure = MepDevicePlacementProposalFailures.Range;
                transformed = [];
                return false;
            }

            if (!seen.Add(new OffsetTriple(offsetX, offsetY, item.VerticalOffset)))
            {
                failure = MepDevicePlacementProposalFailures.Duplicate;
                transformed = [];
                return false;
            }

            transformed.Add(new MepDevicePlacementProposalItem(
                offsetX,
                offsetY,
                item.VerticalOffset,
                item.SourceEvidence,
                item.Confidence,
                pageX,
                pageY));
        }

        return transformed.Count == placements.Count;
    }

    private readonly record struct OffsetTriple(double X, double Y, double Z);
}
