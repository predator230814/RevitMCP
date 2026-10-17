using Autodesk.Revit.DB;
using BimIt.Revit.Addin.Intents;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Addin.Capabilities;

internal static class RevitMepDeviceTemplateReader
{
    public static MepTemplateFacts Read(Document document, string templateRef, string lengthUnitId)
    {
        Element? element;
        try
        {
            element = document.GetElement(templateRef);
        }
        catch (Exception)
        {
            element = null;
        }

        if (element is null)
        {
            return new MepTemplateFacts { Resolved = false };
        }

        if (element is not FamilyInstance instance)
        {
            return new MepTemplateFacts { Resolved = true, IsFamilyInstance = false };
        }

        var categoryToken = ReadCategoryToken(instance);
        var symbol = TrySymbol(instance);
        var level = TryLevel(document, instance);
        var mode = symbol is null ? null : TryPlacementMode(symbol, instance, document, level);
        var host = TryHost(instance);
        var hostInDocument = host is not null && EqualityComparer<Document>.Default.Equals(host.Document, document);
        var hostIsWall = host is Wall;
        Line? line = null;
        var straight = host is Wall wall && TryStraightLine(wall, out line);
        var hasPoint = instance.Location is LocationPoint location && location.Point is not null && double.IsFinite(location.Rotation);
        var names = ReadNames(instance, symbol, host as Wall, level);
        var orientation = TryOrientation(instance);
        var wallBinding = TryWallBinding(host as Wall, line, names?.Host);
        var referenceDirection = TryReferenceDirection(orientation);

        var canSnapshot = MepDeviceCategorySupport.IsSupported(categoryToken)
            && symbol is not null
            && !string.IsNullOrEmpty(symbol.UniqueId)
            && hasPoint
            && level is not null
            && !string.IsNullOrEmpty(level.UniqueId)
            && names is not null
            && orientation is not null
            && mode is PreviewMepDevicePlacementMode.LevelBased
                or PreviewMepDevicePlacementMode.WallHosted
                or PreviewMepDevicePlacementMode.WorkPlaneBasedLevel;

        if (mode == PreviewMepDevicePlacementMode.WallHosted)
        {
            canSnapshot = canSnapshot && hostInDocument && hostIsWall && straight && wallBinding is not null;
        }
        else if (mode == PreviewMepDevicePlacementMode.WorkPlaneBasedLevel)
        {
            canSnapshot = canSnapshot && referenceDirection is not null;
        }

        MepDevicePlacementIntent? snapshot = null;
        var occupied = Array.Empty<MepPoint>();
        var occupancyReadSucceeded = false;
        if (canSnapshot && names is not null && symbol is not null && level is not null && orientation is not null)
        {
            var point = ((LocationPoint)instance.Location).Point;
            var strategy = ToStrategy(mode!.Value);
            snapshot = new MepDevicePlacementIntent(
                strategy,
                lengthUnitId,
                templateRef,
                symbol.UniqueId,
                categoryToken!,
                names.Value.Category,
                names.Value.Family,
                names.Value.Type,
                level.UniqueId,
                names.Value.Level,
                new MepPoint(point.X, point.Y, point.Z),
                orientation.Value,
                strategy == MepPlacementStrategyKind.WallHosted ? wallBinding : null,
                strategy == MepPlacementStrategyKind.WorkPlaneBasedLevel ? referenceDirection : null,
                Array.Empty<MepDevicePlacementItem>());
            occupancyReadSucceeded = TryReadOccupants(
                document,
                symbol,
                strategy,
                host as Wall,
                level,
                snapshot.TemplateLocation,
                out occupied);
        }

        return new MepTemplateFacts
        {
            Resolved = true,
            IsFamilyInstance = true,
            CategoryToken = categoryToken,
            HasSymbol = symbol is not null && !string.IsNullOrEmpty(symbol.UniqueId),
            PlacementMode = mode,
            HostInDocument = hostInDocument,
            HostIsWall = hostIsWall,
            HostIsStraightLine = straight,
            HasLocationPoint = hasPoint,
            HasLevel = level is not null && !string.IsNullOrEmpty(level.UniqueId),
            CategoryName = names?.Category,
            FamilyName = names?.Family,
            TypeName = names?.Type,
            LevelName = names?.Level,
            HostElementRef = mode == PreviewMepDevicePlacementMode.WallHosted && host is Wall hostWall && !string.IsNullOrEmpty(hostWall.UniqueId)
                ? hostWall.UniqueId
                : null,
            HostName = mode == PreviewMepDevicePlacementMode.WallHosted ? names?.Host : null,
            Snapshot = snapshot,
            OccupancyReadSucceeded = occupancyReadSucceeded,
            OccupiedPoints = occupied
        };
    }

    internal static bool IsCurrentLevelWorkPlane(FamilyInstance instance, Document document, Level level)
    {
        return TryPlacementMode(instance.Symbol, instance, document, level) == PreviewMepDevicePlacementMode.WorkPlaneBasedLevel;
    }

    private static MepPlacementStrategyKind ToStrategy(PreviewMepDevicePlacementMode mode)
    {
        return mode switch
        {
            PreviewMepDevicePlacementMode.LevelBased => MepPlacementStrategyKind.LevelBased,
            PreviewMepDevicePlacementMode.WallHosted => MepPlacementStrategyKind.WallHosted,
            PreviewMepDevicePlacementMode.WorkPlaneBasedLevel => MepPlacementStrategyKind.WorkPlaneBasedLevel,
            _ => throw new InvalidOperationException("Only creatable placement modes have a strategy.")
        };
    }

    private static PreviewMepDevicePlacementMode? TryPlacementMode(
        FamilySymbol symbol,
        FamilyInstance instance,
        Document document,
        Level? level)
    {
        FamilyPlacementType placement;
        try
        {
            placement = symbol.Family.FamilyPlacementType;
        }
        catch (Exception)
        {
            return null;
        }

        if (placement == FamilyPlacementType.OneLevelBased)
        {
            return PreviewMepDevicePlacementMode.LevelBased;
        }

        if (placement == FamilyPlacementType.OneLevelBasedHosted)
        {
            return PreviewMepDevicePlacementMode.WallHosted;
        }

        if (placement != FamilyPlacementType.WorkPlaneBased)
        {
            return null;
        }

        bool hostFacePresent;
        try
        {
            hostFacePresent = instance.HostFace is not null;
        }
        catch (Exception)
        {
            return PreviewMepDevicePlacementMode.WorkPlaneBasedOther;
        }

        var levelPlane = !hostFacePresent && TryLevelPlaneReference(instance, document, level);
        return MepWorkPlaneClassification.Classify(hostFacePresent, levelPlane);
    }

    /// <summary>
    /// True only when the sketch-plane element reference is the associated level plane reference.
    /// Display names and SKETCH_PLANE_PARAM text are not consulted.
    /// </summary>
    private static bool TryLevelPlaneReference(FamilyInstance instance, Document document, Level? level)
    {
        if (level is null)
        {
            return false;
        }

        try
        {
            var parameter = instance.get_Parameter(BuiltInParameter.SKETCH_PLANE_PARAM);
            if (parameter is null)
            {
                return false;
            }

            var sketchId = parameter.AsElementId();
            if (sketchId is null || sketchId == ElementId.InvalidElementId)
            {
                return false;
            }

            if (document.GetElement(sketchId) is not SketchPlane sketch)
            {
                return false;
            }

            var sketchReference = sketch.GetPlaneReference();
            var levelReference = level.GetPlaneReference();
            return sketchReference is not null
                && levelReference is not null
                && sketchReference.EqualTo(levelReference);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string? ReadCategoryToken(FamilyInstance instance)
    {
        try
        {
            var category = instance.Category;
            return category is null ? null : category.BuiltInCategory.ToString();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static FamilySymbol? TrySymbol(FamilyInstance instance)
    {
        try
        {
            return instance.Symbol;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static Element? TryHost(FamilyInstance instance)
    {
        try
        {
            return instance.Host;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool TryStraightLine(Wall wall, out Line? line)
    {
        line = null;
        try
        {
            if (wall.Location is not LocationCurve location || location.Curve is not Line candidate || !candidate.IsBound)
            {
                return false;
            }

            var start = candidate.GetEndPoint(0);
            var end = candidate.GetEndPoint(1);
            var plan = Math.Sqrt(Math.Pow(end.X - start.X, 2) + Math.Pow(end.Y - start.Y, 2));
            if (!double.IsFinite(plan) || plan <= MepDevicePlacementGeometry.OccupancyToleranceFeet)
            {
                return false;
            }

            line = candidate;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static Level? TryLevel(Document document, FamilyInstance instance)
    {
        try
        {
            if (instance.LevelId is null || instance.LevelId == ElementId.InvalidElementId)
            {
                return null;
            }

            return document.GetElement(instance.LevelId) as Level;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static MepOrientation? TryOrientation(FamilyInstance instance)
    {
        try
        {
            if (instance.Location is not LocationPoint location || !double.IsFinite(location.Rotation))
            {
                return null;
            }

            return new MepOrientation(
                location.Rotation,
                instance.FacingFlipped,
                instance.HandFlipped,
                instance.CanFlipFacing,
                instance.CanFlipHand);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static MepPlanDirection? TryReferenceDirection(MepOrientation? orientation)
    {
        return orientation is { } value && MepDevicePlacementGeometry.TryPlanDirection(value.RotationRadians, out var direction)
            ? direction
            : null;
    }

    private static MepWallBinding? TryWallBinding(Wall? wall, Line? line, string? hostName)
    {
        if (wall is null || line is null || string.IsNullOrEmpty(wall.UniqueId) || string.IsNullOrEmpty(hostName))
        {
            return null;
        }

        var start = line.GetEndPoint(0);
        var end = line.GetEndPoint(1);
        return new MepWallBinding(
            wall.UniqueId,
            hostName,
            new MepPoint(start.X, start.Y, start.Z),
            new MepPoint(end.X, end.Y, end.Z));
    }

    private static (string Category, string Family, string Type, string Host, string Level)? ReadNames(
        FamilyInstance instance,
        FamilySymbol? symbol,
        Wall? wall,
        Level? level)
    {
        var category = Bound(instance.Category?.Name);
        var family = Bound(symbol?.FamilyName);
        var type = Bound(symbol?.Name);
        var host = Bound(wall?.Name);
        var levelName = Bound(level?.Name);
        if (category.Length == 0 || family.Length == 0 || type.Length == 0 || levelName.Length == 0)
        {
            return null;
        }

        return (category, family, type, host, levelName);
    }

    private static string Bound(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Length <= MepDevicePlacementCanonicalEncoder.MaxTextLength
            ? value
            : value[..MepDevicePlacementCanonicalEncoder.MaxTextLength];
    }

    private static bool TryReadOccupants(
        Document document,
        FamilySymbol symbol,
        MepPlacementStrategyKind strategy,
        Wall? wall,
        Level level,
        MepPoint templateLocation,
        out MepPoint[] points)
    {
        points = [];
        var collected = new List<MepPoint> { templateLocation };
        try
        {
            var filter = new FamilyInstanceFilter(document, symbol.Id);
            foreach (var element in new FilteredElementCollector(document).WherePasses(filter).OfClass(typeof(FamilyInstance)))
            {
                if (element is not FamilyInstance occupant || occupant.Location is not LocationPoint location || location.Point is not XYZ point)
                {
                    continue;
                }

                if (!Occupies(document, strategy, wall, level, occupant))
                {
                    continue;
                }

                collected.Add(new MepPoint(point.X, point.Y, point.Z));
            }
        }
        catch (Exception)
        {
            return false;
        }

        points = collected.ToArray();
        return true;
    }

    private static bool Occupies(Document document, MepPlacementStrategyKind strategy, Wall? wall, Level level, FamilyInstance occupant)
    {
        if (occupant.LevelId != level.Id)
        {
            return false;
        }

        return strategy switch
        {
            MepPlacementStrategyKind.LevelBased => true,
            MepPlacementStrategyKind.WallHosted => wall is not null && occupant.Host is not null && occupant.Host.Id == wall.Id,
            MepPlacementStrategyKind.WorkPlaneBasedLevel => TryPlacementMode(occupant.Symbol, occupant, document, level) == PreviewMepDevicePlacementMode.WorkPlaneBasedLevel,
            _ => false
        };
    }
}
