using Autodesk.Revit.DB;
using BimIt.Revit.Addin.Intents;

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
        var supportedCategory = MepDeviceCategorySupport.IsSupported(categoryToken);
        var symbol = TrySymbol(instance);
        var placementSupported = symbol is not null && IsWallHostedPlacement(symbol);
        var host = TryHost(instance);
        var hostInDocument = host is not null && EqualityComparer<Document>.Default.Equals(host.Document, document);
        var hostIsWall = host is Wall;
        Line? line = null;
        var straight = host is Wall wall && TryStraightLine(wall, out line);
        var hasPoint = instance.Location is LocationPoint location && location.Point is not null;
        var level = TryLevel(document, instance);
        var names = ReadNames(instance, symbol, host as Wall, level);
        var canSnapshot = supportedCategory
            && placementSupported
            && hostInDocument
            && hostIsWall
            && straight
            && hasPoint
            && level is not null
            && symbol is not null
            && line is not null
            && names is not null;

        MepDevicePlacementIntent? snapshot = null;
        var occupied = Array.Empty<MepPoint>();
        if (canSnapshot && names is not null && symbol is not null && host is Wall hostWall && line is not null && level is not null)
        {
            var point = ((LocationPoint)instance.Location).Point;
            var start = line.GetEndPoint(0);
            var end = line.GetEndPoint(1);
            snapshot = new MepDevicePlacementIntent(
                lengthUnitId,
                templateRef,
                symbol.UniqueId,
                categoryToken!,
                names.Value.Category,
                names.Value.Family,
                names.Value.Type,
                hostWall.UniqueId,
                names.Value.Host,
                level.UniqueId,
                names.Value.Level,
                new MepPoint(point.X, point.Y, point.Z),
                new MepPoint(start.X, start.Y, start.Z),
                new MepPoint(end.X, end.Y, end.Z),
                instance.FacingFlipped,
                instance.HandFlipped,
                instance.CanFlipFacing,
                instance.CanFlipHand,
                Array.Empty<MepDevicePlacementItem>());
            occupied = ReadOccupants(document, symbol, hostWall, snapshot.TemplateLocation);
        }

        return new MepTemplateFacts
        {
            Resolved = true,
            IsFamilyInstance = true,
            CategoryToken = categoryToken,
            SupportedPlacementMode = placementSupported,
            HasSymbol = symbol is not null && !string.IsNullOrEmpty(symbol.UniqueId),
            HostInDocument = hostInDocument,
            HostIsWall = hostIsWall,
            HostIsStraightLine = straight,
            HasLocationPoint = hasPoint,
            HasLevel = level is not null && !string.IsNullOrEmpty(level.UniqueId),
            Snapshot = snapshot,
            OccupiedPoints = occupied
        };
    }

    private static string? ReadCategoryToken(FamilyInstance instance)
    {
        try
        {
            var category = instance.Category;
            if (category is null)
            {
                return null;
            }

            return category.BuiltInCategory.ToString();
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

    private static bool IsWallHostedPlacement(FamilySymbol symbol)
    {
        try
        {
            return symbol.Family?.FamilyPlacementType == FamilyPlacementType.OneLevelBasedHosted;
        }
        catch (Exception)
        {
            return false;
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
        if (category.Length == 0 || family.Length == 0 || type.Length == 0 || host.Length == 0 || levelName.Length == 0)
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

    private static MepPoint[] ReadOccupants(Document document, FamilySymbol symbol, Wall wall, MepPoint templateLocation)
    {
        var points = new List<MepPoint> { templateLocation };
        try
        {
            var filter = new FamilyInstanceFilter(document, symbol.Id);
            foreach (var element in new FilteredElementCollector(document).WherePasses(filter).OfClass(typeof(FamilyInstance)))
            {
                if (element is not FamilyInstance occupant)
                {
                    continue;
                }

                if (occupant.Host is null || occupant.Host.Id != wall.Id)
                {
                    continue;
                }

                if (occupant.Location is LocationPoint location && location.Point is XYZ point)
                {
                    points.Add(new MepPoint(point.X, point.Y, point.Z));
                }
            }
        }
        catch (Exception)
        {
            return points.ToArray();
        }

        return points.ToArray();
    }
}
