using BimIt.Revit.Contracts;

namespace BimIt.Revit.Addin.Intents;

internal sealed class MepTemplateFacts
{
    public bool Resolved { get; init; }

    public bool IsFamilyInstance { get; init; }

    public string? CategoryToken { get; init; }

    public bool SupportedPlacementMode { get; init; }

    public bool HasSymbol { get; init; }

    public bool HostInDocument { get; init; }

    public bool HostIsWall { get; init; }

    public bool HostIsStraightLine { get; init; }

    public bool HasLocationPoint { get; init; }

    public bool HasLevel { get; init; }

    public MepDevicePlacementIntent? Snapshot { get; init; }

    public bool OccupancyReadSucceeded { get; init; }

    public IReadOnlyList<MepPoint> OccupiedPoints { get; init; } = Array.Empty<MepPoint>();
}

internal readonly record struct MepConvertedPlacement(double OffsetAlongHost, double VerticalOffset, double AlongHostFeet, double VerticalFeet);

internal static class MepDevicePlacementPreviewComposer
{
    public static PreviewMepDeviceTemplateStatus Classify(MepTemplateFacts facts)
    {
        if (!facts.Resolved)
        {
            return PreviewMepDeviceTemplateStatus.NotFound;
        }

        if (!facts.IsFamilyInstance)
        {
            return PreviewMepDeviceTemplateStatus.NotFamilyInstance;
        }

        if (!MepDeviceCategorySupport.IsSupported(facts.CategoryToken))
        {
            return PreviewMepDeviceTemplateStatus.UnsupportedCategory;
        }

        if (!facts.SupportedPlacementMode || !facts.HasSymbol)
        {
            return PreviewMepDeviceTemplateStatus.UnsupportedPlacement;
        }

        if (!facts.HostInDocument || !facts.HostIsWall)
        {
            return PreviewMepDeviceTemplateStatus.UnsupportedHost;
        }

        if (!facts.HostIsStraightLine)
        {
            return PreviewMepDeviceTemplateStatus.UnsupportedHostGeometry;
        }

        if (!facts.HasLocationPoint)
        {
            return PreviewMepDeviceTemplateStatus.UnsupportedLocation;
        }

        if (!facts.HasLevel)
        {
            return PreviewMepDeviceTemplateStatus.UnsupportedLevel;
        }

        if (facts.Snapshot is null)
        {
            return PreviewMepDeviceTemplateStatus.UnsupportedPlacement;
        }

        return PreviewMepDeviceTemplateStatus.Ok;
    }

    public static bool TryEvaluate(
        string requestedTemplateRef,
        MepTemplateFacts facts,
        IReadOnlyList<MepConvertedPlacement> placements,
        out PreviewMepDeviceTemplate template,
        out IReadOnlyList<PreviewMepDevicePlacementItem> items,
        out MepDevicePlacementIntent? intent)
    {
        template = null!;
        items = Array.Empty<PreviewMepDevicePlacementItem>();
        intent = null;
        var status = Classify(facts);
        var snapshot = facts.Snapshot;
        if (status != PreviewMepDeviceTemplateStatus.Ok || snapshot is null)
        {
            template = new PreviewMepDeviceTemplate
            {
                ElementRef = requestedTemplateRef,
                Status = status
            };
            items = Unevaluated(placements);
            return true;
        }

        if (!facts.OccupancyReadSucceeded)
        {
            template = new PreviewMepDeviceTemplate
            {
                ElementRef = snapshot.TemplateElementRef,
                Status = PreviewMepDeviceTemplateStatus.Ok,
                CategoryName = snapshot.CategoryName,
                FamilyName = snapshot.FamilyName,
                TypeName = snapshot.TypeName,
                LevelName = snapshot.LevelName,
                HostElementRef = snapshot.HostElementRef,
                HostName = snapshot.HostName
            };
            items = Unevaluated(placements);
            return true;
        }

        template = new PreviewMepDeviceTemplate
        {
            ElementRef = snapshot.TemplateElementRef,
            Status = PreviewMepDeviceTemplateStatus.Ok,
            CategoryName = snapshot.CategoryName,
            FamilyName = snapshot.FamilyName,
            TypeName = snapshot.TypeName,
            LevelName = snapshot.LevelName,
            HostElementRef = snapshot.HostElementRef,
            HostName = snapshot.HostName
        };

        var rendered = new PreviewMepDevicePlacementItem[placements.Count];
        var stored = new MepDevicePlacementItem[placements.Count];
        var occupants = new List<MepPoint>(facts.OccupiedPoints);
        var ready = true;
        for (var index = 0; index < placements.Count; index++)
        {
            var placement = placements[index];
            var itemStatus = ClassifyItem(snapshot, occupants, placement, out var target);
            if (itemStatus == PreviewMepDevicePlacementItemStatus.Ok)
            {
                occupants.Add(target);
            }
            rendered[index] = new PreviewMepDevicePlacementItem
            {
                OffsetAlongHost = placement.OffsetAlongHost,
                VerticalOffset = placement.VerticalOffset,
                Status = itemStatus
            };
            if (itemStatus != PreviewMepDevicePlacementItemStatus.Ok)
            {
                ready = false;
                continue;
            }

            stored[index] = new MepDevicePlacementItem(index + 1, placement.OffsetAlongHost, placement.VerticalOffset, target);
        }

        items = rendered;
        if (!ready)
        {
            return true;
        }

        intent = new MepDevicePlacementIntent(
            snapshot.LengthUnitId,
            snapshot.TemplateElementRef,
            snapshot.SymbolElementRef,
            snapshot.CategoryToken,
            snapshot.CategoryName,
            snapshot.FamilyName,
            snapshot.TypeName,
            snapshot.HostElementRef,
            snapshot.HostName,
            snapshot.LevelElementRef,
            snapshot.LevelName,
            snapshot.TemplateLocation,
            snapshot.HostStart,
            snapshot.HostEnd,
            snapshot.FacingFlipped,
            snapshot.HandFlipped,
            snapshot.CanFlipFacing,
            snapshot.CanFlipHand,
            stored);
        return true;
    }

    public static bool IsCurrent(MepDevicePlacementIntent approved, MepTemplateFacts live)
    {
        if (!live.OccupancyReadSucceeded
            || Classify(live) != PreviewMepDeviceTemplateStatus.Ok
            || live.Snapshot is not { } snapshot)
        {
            return false;
        }

        if (!string.Equals(snapshot.TemplateElementRef, approved.TemplateElementRef, StringComparison.Ordinal)
            || !string.Equals(snapshot.SymbolElementRef, approved.SymbolElementRef, StringComparison.Ordinal)
            || !string.Equals(snapshot.CategoryToken, approved.CategoryToken, StringComparison.Ordinal)
            || !string.Equals(snapshot.CategoryName, approved.CategoryName, StringComparison.Ordinal)
            || !string.Equals(snapshot.FamilyName, approved.FamilyName, StringComparison.Ordinal)
            || !string.Equals(snapshot.TypeName, approved.TypeName, StringComparison.Ordinal)
            || !string.Equals(snapshot.HostElementRef, approved.HostElementRef, StringComparison.Ordinal)
            || !string.Equals(snapshot.HostName, approved.HostName, StringComparison.Ordinal)
            || !string.Equals(snapshot.LevelElementRef, approved.LevelElementRef, StringComparison.Ordinal)
            || !string.Equals(snapshot.LevelName, approved.LevelName, StringComparison.Ordinal)
            || !string.Equals(snapshot.LengthUnitId, approved.LengthUnitId, StringComparison.Ordinal)
            || snapshot.FacingFlipped != approved.FacingFlipped
            || snapshot.HandFlipped != approved.HandFlipped
            || snapshot.CanFlipFacing != approved.CanFlipFacing
            || snapshot.CanFlipHand != approved.CanFlipHand
            || !MepDevicePlacementGeometry.SamePoint(snapshot.TemplateLocation, approved.TemplateLocation)
            || !MepDevicePlacementGeometry.SamePoint(snapshot.HostStart, approved.HostStart)
            || !MepDevicePlacementGeometry.SamePoint(snapshot.HostEnd, approved.HostEnd))
        {
            return false;
        }

        foreach (var item in approved.Items)
        {
            if (MepDevicePlacementGeometry.IsOccupied(item.Target, live.OccupiedPoints))
            {
                return false;
            }
        }

        return true;
    }

    private static PreviewMepDevicePlacementItemStatus ClassifyItem(
        MepDevicePlacementIntent snapshot,
        IReadOnlyList<MepPoint> occupants,
        MepConvertedPlacement placement,
        out MepPoint target)
    {
        if (!MepDevicePlacementGeometry.TryTarget(
                snapshot.TemplateLocation,
                snapshot.HostStart,
                snapshot.HostEnd,
                placement.AlongHostFeet,
                placement.VerticalFeet,
                out target))
        {
            return PreviewMepDevicePlacementItemStatus.UnsupportedPosition;
        }

        if (!MepDevicePlacementGeometry.ProjectsWithinHost(snapshot.HostStart, snapshot.HostEnd, target))
        {
            return PreviewMepDevicePlacementItemStatus.OutOfHostBounds;
        }

        if (MepDevicePlacementGeometry.IsOccupied(target, occupants))
        {
            return PreviewMepDevicePlacementItemStatus.Occupied;
        }

        return PreviewMepDevicePlacementItemStatus.Ok;
    }

    private static IReadOnlyList<PreviewMepDevicePlacementItem> Unevaluated(IReadOnlyList<MepConvertedPlacement> placements)
    {
        var items = new PreviewMepDevicePlacementItem[placements.Count];
        for (var index = 0; index < placements.Count; index++)
        {
            items[index] = new PreviewMepDevicePlacementItem
            {
                OffsetAlongHost = placements[index].OffsetAlongHost,
                VerticalOffset = placements[index].VerticalOffset,
                Status = PreviewMepDevicePlacementItemStatus.UnsupportedPosition
            };
        }

        return items;
    }
}
