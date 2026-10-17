using BimIt.Revit.Bridge;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Addin.Inspection;

internal static class PreviewMepDevicePlacementsRequestValidator
{
    public const int MinPlacements = 1;
    public const int MaxPlacements = 20;
    public const int MaxRefLength = 128;
    public const int MaxUnitLength = 256;

    public static void Validate(PreviewMepDevicePlacementsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsBounded(request.DocumentId, MaxRefLength)
            || !IsBounded(request.TemplateElementRef, MaxRefLength)
            || !IsBounded(request.LengthUnitId, MaxUnitLength)
            || request.Placements is null
            || request.Placements.Count is < MinPlacements or > MaxPlacements)
        {
            throw Invalid();
        }

        var triples = new HashSet<(double X, double Y, double Vertical)>();
        foreach (var placement in request.Placements)
        {
            if (placement is null
                || !double.IsFinite(placement.OffsetX)
                || !double.IsFinite(placement.OffsetY)
                || !double.IsFinite(placement.VerticalOffset)
                || !triples.Add((placement.OffsetX, placement.OffsetY, placement.VerticalOffset)))
            {
                throw Invalid();
            }
        }
    }

    private static bool IsBounded(string? value, int max)
    {
        return !string.IsNullOrWhiteSpace(value) && value.Length <= max && value.All(character => !char.IsWhiteSpace(character));
    }

    private static BridgeException Invalid()
    {
        return new BridgeException(
            CapabilityErrorCodes.InvalidMepDevicePlacement,
            "The MEP device placement request is invalid.");
    }
}
