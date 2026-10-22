namespace BimIt.Revit.Assistant.Patterns;

internal static class PageFrame
{
    public static SourceRoomPattern ForMatching(SourceRoomPattern page, int rasterWidth, int rasterHeight)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (rasterWidth <= 0 || rasterHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rasterWidth));
        }

        return new SourceRoomPattern(
            page.Boundary.Select(point => ToCartesian(point.X, point.Y, rasterWidth, rasterHeight)).ToArray(),
            page.Entrance is PlanPoint entrance
                ? ToCartesian(entrance.X, entrance.Y, rasterWidth, rasterHeight)
                : null,
            page.Devices.Select(device =>
            {
                var point = ToCartesian(device.X, device.Y, rasterWidth, rasterHeight);
                return new SourceDevice(device.Role, point.X, point.Y, device.PlacementKind, device.FamilyHint);
            }).ToArray());
    }

    // Page coordinates are normalized and Y-down. Matching uses the raster's Y-up frame.
    public static PlanPoint ToCartesian(double pageX, double pageY, int rasterWidth, int rasterHeight)
    {
        return new PlanPoint(pageX * rasterWidth, (1 - pageY) * rasterHeight);
    }
}
