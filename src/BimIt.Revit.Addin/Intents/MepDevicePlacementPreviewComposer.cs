using BimIt.Revit.Contracts;

namespace BimIt.Revit.Addin.Intents;

internal sealed class MepTemplateFacts
{
    public bool Resolved { get; init; }

    public bool IsFamilyInstance { get; init; }

    public string? CategoryToken { get; init; }

    public bool HasSymbol { get; init; }

    public PreviewMepDevicePlacementMode? PlacementMode { get; init; }

    public bool HostInDocument { get; init; }

    public bool HostIsWall { get; init; }

    public bool HostIsStraightLine { get; init; }

    public bool HasLocationPoint { get; init; }

    public bool HasLevel { get; init; }

    public string? CategoryName { get; init; }

    public string? FamilyName { get; init; }

    public string? TypeName { get; init; }

    public string? LevelName { get; init; }

    public string? HostElementRef { get; init; }

    public string? HostName { get; init; }

    public MepDevicePlacementIntent? Snapshot { get; init; }

    public bool OccupancyReadSucceeded { get; init; }

    public IReadOnlyList<MepPoint> OccupiedPoints { get; init; } = Array.Empty<MepPoint>();
}

internal readonly record struct MepConvertedPlacement(
    double OffsetX,
    double OffsetY,
    double VerticalOffset,
    double OffsetXFeet,
    double OffsetYFeet,
    double VerticalFeet);

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

        if (facts.PlacementMode is null || !facts.HasSymbol)
        {
            return PreviewMepDeviceTemplateStatus.UnsupportedPlacement;
        }

        if (facts.PlacementMode is PreviewMepDevicePlacementMode.WorkPlaneBasedFace
            or PreviewMepDevicePlacementMode.WorkPlaneBasedOther)
        {
            return PreviewMepDeviceTemplateStatus.UnsupportedPlacement;
        }

        if (facts.PlacementMode == PreviewMepDevicePlacementMode.WallHosted)
        {
            if (!facts.HostInDocument || !facts.HostIsWall)
            {
                return PreviewMepDeviceTemplateStatus.UnsupportedHost;
            }

            if (!facts.HostIsStraightLine)
            {
                return PreviewMepDeviceTemplateStatus.UnsupportedHostGeometry;
            }
        }
        else if (facts.PlacementMode is not (
            PreviewMepDevicePlacementMode.LevelBased
            or PreviewMepDevicePlacementMode.WorkPlaneBasedLevel))
        {
            return PreviewMepDeviceTemplateStatus.UnsupportedPlacement;
        }

        if (!facts.HasLocationPoint)
        {
            return PreviewMepDeviceTemplateStatus.UnsupportedLocation;
        }

        if (!facts.HasLevel)
        {
            return PreviewMepDeviceTemplateStatus.UnsupportedLevel;
        }

        if (facts.Snapshot is null || !StrategyMatches(facts.PlacementMode.Value, facts.Snapshot.Strategy))
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
        template = Describe(requestedTemplateRef, status, facts);
        var snapshot = facts.Snapshot;
        if (status != PreviewMepDeviceTemplateStatus.Ok || snapshot is null || !facts.OccupancyReadSucceeded)
        {
            items = Unevaluated(placements);
            return true;
        }

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
                OffsetX = placement.OffsetX,
                OffsetY = placement.OffsetY,
                VerticalOffset = placement.VerticalOffset,
                Status = itemStatus
            };
            if (itemStatus != PreviewMepDevicePlacementItemStatus.Ok)
            {
                ready = false;
                continue;
            }

            stored[index] = new MepDevicePlacementItem(
                index + 1,
                placement.OffsetX,
                placement.OffsetY,
                placement.VerticalOffset,
                target);
        }

        items = rendered;
        if (!ready)
        {
            return true;
        }

        intent = new MepDevicePlacementIntent(
            snapshot.Strategy,
            snapshot.LengthUnitId,
            snapshot.TemplateElementRef,
            snapshot.SymbolElementRef,
            snapshot.CategoryToken,
            snapshot.CategoryName,
            snapshot.FamilyName,
            snapshot.TypeName,
            snapshot.LevelElementRef,
            snapshot.LevelName,
            snapshot.TemplateLocation,
            snapshot.Orientation,
            snapshot.Wall,
            snapshot.ReferenceDirection,
            stored);
        return true;
    }

    public static bool IsCurrent(MepDevicePlacementIntent approved, MepTemplateFacts live)
    {
        if (!live.OccupancyReadSucceeded
            || Classify(live) != PreviewMepDeviceTemplateStatus.Ok
            || live.Snapshot is not { } snapshot
            || snapshot.Strategy != approved.Strategy
            || !string.Equals(snapshot.TemplateElementRef, approved.TemplateElementRef, StringComparison.Ordinal)
            || !string.Equals(snapshot.SymbolElementRef, approved.SymbolElementRef, StringComparison.Ordinal)
            || !string.Equals(snapshot.CategoryToken, approved.CategoryToken, StringComparison.Ordinal)
            || !string.Equals(snapshot.CategoryName, approved.CategoryName, StringComparison.Ordinal)
            || !string.Equals(snapshot.FamilyName, approved.FamilyName, StringComparison.Ordinal)
            || !string.Equals(snapshot.TypeName, approved.TypeName, StringComparison.Ordinal)
            || !string.Equals(snapshot.LevelElementRef, approved.LevelElementRef, StringComparison.Ordinal)
            || !string.Equals(snapshot.LevelName, approved.LevelName, StringComparison.Ordinal)
            || !string.Equals(snapshot.LengthUnitId, approved.LengthUnitId, StringComparison.Ordinal)
            || !MepDevicePlacementGeometry.SameAngle(snapshot.Orientation.RotationRadians, approved.Orientation.RotationRadians)
            || snapshot.Orientation.FacingFlipped != approved.Orientation.FacingFlipped
            || snapshot.Orientation.HandFlipped != approved.Orientation.HandFlipped
            || snapshot.Orientation.CanFlipFacing != approved.Orientation.CanFlipFacing
            || snapshot.Orientation.CanFlipHand != approved.Orientation.CanFlipHand
            || !MepDevicePlacementGeometry.SamePoint(snapshot.TemplateLocation, approved.TemplateLocation))
        {
            return false;
        }

        if (approved.Strategy == MepPlacementStrategyKind.WallHosted)
        {
            if (snapshot.Wall is null
                || approved.Wall is null
                || !string.Equals(snapshot.Wall.HostElementRef, approved.Wall.HostElementRef, StringComparison.Ordinal)
                || !string.Equals(snapshot.Wall.HostName, approved.Wall.HostName, StringComparison.Ordinal)
                || !MepDevicePlacementGeometry.SamePoint(snapshot.Wall.HostStart, approved.Wall.HostStart)
                || !MepDevicePlacementGeometry.SamePoint(snapshot.Wall.HostEnd, approved.Wall.HostEnd))
            {
                return false;
            }
        }

        if (approved.Strategy == MepPlacementStrategyKind.WorkPlaneBasedLevel)
        {
            if (snapshot.ReferenceDirection is not { } liveDirection
                || approved.ReferenceDirection is not { } approvedDirection
                || !MepDevicePlacementGeometry.SameDirection(liveDirection, approvedDirection))
            {
                return false;
            }
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

    private static bool StrategyMatches(PreviewMepDevicePlacementMode mode, MepPlacementStrategyKind strategy)
    {
        return mode switch
        {
            PreviewMepDevicePlacementMode.LevelBased => strategy == MepPlacementStrategyKind.LevelBased,
            PreviewMepDevicePlacementMode.WallHosted => strategy == MepPlacementStrategyKind.WallHosted,
            PreviewMepDevicePlacementMode.WorkPlaneBasedLevel => strategy == MepPlacementStrategyKind.WorkPlaneBasedLevel,
            _ => false
        };
    }

    private static PreviewMepDeviceTemplate Describe(string requestedTemplateRef, PreviewMepDeviceTemplateStatus status, MepTemplateFacts facts)
    {
        var snapshot = facts.Snapshot;
        var showIdentity = snapshot is not null || facts.PlacementMode is not null;
        return new PreviewMepDeviceTemplate
        {
            ElementRef = snapshot?.TemplateElementRef ?? requestedTemplateRef,
            Status = status,
            PlacementMode = facts.PlacementMode,
            CategoryName = showIdentity ? facts.CategoryName ?? snapshot?.CategoryName : null,
            FamilyName = showIdentity ? facts.FamilyName ?? snapshot?.FamilyName : null,
            TypeName = showIdentity ? facts.TypeName ?? snapshot?.TypeName : null,
            LevelName = showIdentity ? facts.LevelName ?? snapshot?.LevelName : null,
            HostElementRef = facts.PlacementMode == PreviewMepDevicePlacementMode.WallHosted
                ? facts.HostElementRef ?? snapshot?.Wall?.HostElementRef
                : null,
            HostName = facts.PlacementMode == PreviewMepDevicePlacementMode.WallHosted
                ? facts.HostName ?? snapshot?.Wall?.HostName
                : null
        };
    }

    private static PreviewMepDevicePlacementItemStatus ClassifyItem(
        MepDevicePlacementIntent snapshot,
        IReadOnlyList<MepPoint> occupants,
        MepConvertedPlacement placement,
        out MepPoint target)
    {
        if (!MepDevicePlacementGeometry.TryProjectTarget(
                snapshot.TemplateLocation,
                placement.OffsetXFeet,
                placement.OffsetYFeet,
                placement.VerticalFeet,
                out target))
        {
            return PreviewMepDevicePlacementItemStatus.UnsupportedPosition;
        }

        if (snapshot.Strategy == MepPlacementStrategyKind.WallHosted)
        {
            if (snapshot.Wall is null
                || !MepDevicePlacementGeometry.TryWallPlacement(
                    snapshot.TemplateLocation,
                    snapshot.Wall.HostStart,
                    snapshot.Wall.HostEnd,
                    target,
                    out var withinSegment))
            {
                return PreviewMepDevicePlacementItemStatus.UnsupportedPosition;
            }

            if (!withinSegment)
            {
                return PreviewMepDevicePlacementItemStatus.OutOfHostBounds;
            }
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
                OffsetX = placements[index].OffsetX,
                OffsetY = placements[index].OffsetY,
                VerticalOffset = placements[index].VerticalOffset,
                Status = PreviewMepDevicePlacementItemStatus.UnsupportedPosition
            };
        }

        return items;
    }
}
