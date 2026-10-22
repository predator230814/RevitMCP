using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using BimIt.Revit.Addin.Execution;
using BimIt.Revit.Addin.Identity;
using BimIt.Revit.Addin.Inspection;
using BimIt.Revit.Bridge;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Addin.Capabilities;

internal sealed class RevitGetRoomLayoutsService : IRevitGetRoomLayoutsService
{
    private readonly RevitExecutionDispatcher _dispatcher;
    private readonly BridgeInstanceMetadata _metadata;
    private readonly OpenDocumentIdentityService _identity;

    public RevitGetRoomLayoutsService(
        RevitExecutionDispatcher dispatcher,
        BridgeInstanceMetadata metadata,
        OpenDocumentIdentityService identity)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(identity);
        _dispatcher = dispatcher;
        _metadata = metadata;
        _identity = identity;
    }

    public async Task<GetRoomLayoutsResult> GetRoomLayoutsAsync(
        GetRoomLayoutsRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var validated = GetRoomLayoutsRequestValidator.Validate(request);
        try
        {
            return await _dispatcher.EnqueueAsync(application => Execute(application, validated), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (BridgeException)
        {
            throw;
        }
        catch (RevitExecutionException)
        {
            throw new BridgeException(
                CapabilityErrorCodes.ExecutionFailed,
                "The Revit room layout request could not be executed.");
        }
        catch (Exception)
        {
            throw new BridgeException(
                CapabilityErrorCodes.ExecutionFailed,
                "The Revit room layout request could not be executed.");
        }
    }

    private GetRoomLayoutsResult Execute(UIApplication application, ValidatedGetRoomLayoutsRequest request)
    {
        var uiDocument = application.ActiveUIDocument;
        if (uiDocument is null)
        {
            throw new BridgeException(
                CapabilityErrorCodes.NoActiveDocument,
                "No active Revit document is available.");
        }

        var document = uiDocument.Document;
        var documentId = _identity.GetId(document);
        if (!string.Equals(request.DocumentId, documentId, StringComparison.Ordinal))
        {
            throw new BridgeException(
                CapabilityErrorCodes.DocumentContextChanged,
                "The supplied document_id does not match the active document.");
        }

        var openings = CollectOpenings(document, out var scanTruncated);
        var items = new List<GetRoomLayoutItem>(request.ElementRefs.Count);
        foreach (var elementRef in request.ElementRefs)
        {
            items.Add(ReadItem(document, elementRef, openings, scanTruncated));
        }

        return new GetRoomLayoutsResult
        {
            Context = new GetRoomLayoutsContext
            {
                InstanceId = _metadata.InstanceId,
                DocumentId = documentId,
            },
            LengthUnit = GetRoomLayoutsResult.Metres,
            ChordToleranceMetres = RoomLayoutLimits.ChordToleranceMetres,
            OpeningScanTruncated = scanTruncated,
            Items = items,
        };
    }

    private static GetRoomLayoutItem ReadItem(
        Document document,
        string elementRef,
        IReadOnlyList<FamilyInstance> openings,
        bool scanTruncated)
    {
        var element = document.GetElement(elementRef);
        if (element is null)
        {
            return Rejected(elementRef, RoomLayoutItemStatus.NotFound);
        }

        if (element is not Room room)
        {
            return Rejected(elementRef, RoomLayoutItemStatus.NotARoom);
        }

        var loops = ReadLoops(room);
        if (!RoomLayoutGeometry.TryBuildPlan(loops, out var plan))
        {
            return Rejected(elementRef, RoomLayoutItemStatus.BoundaryUnavailable);
        }

        var features = ReadFeatures(room, openings, out var featuresTruncated);
        return new GetRoomLayoutItem
        {
            ElementRef = elementRef,
            Status = RoomLayoutItemStatus.Ok,
            Name = BoundText(room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString()),
            Number = BoundText(FirstText(room.get_Parameter(BuiltInParameter.ROOM_NUMBER)?.AsString(), room.Number)),
            LevelName = BoundText((document.GetElement(room.LevelId) as Level)?.Name),
            CentroidXMetres = RoomLayoutGeometry.Round(plan.Centroid.X),
            CentroidYMetres = RoomLayoutGeometry.Round(plan.Centroid.Y),
            LoopsTruncated = plan.LoopsTruncated || plan.Loops.Any(loop => loop.VerticesTruncated),
            FeaturesTruncated = featuresTruncated || scanTruncated,
            Loops = plan.Loops.Select(loop => new GetRoomLayoutLoop
            {
                VerticesTruncated = loop.VerticesTruncated,
                Vertices = loop.Vertices.Select(point => new GetRoomLayoutVertex
                {
                    XMetres = point.X,
                    YMetres = point.Y,
                }).ToArray(),
            }).ToArray(),
            Features = features,
        };
    }

    private static List<IReadOnlyList<PlanPoint>> ReadLoops(Room room)
    {
        var options = new SpatialElementBoundaryOptions
        {
            SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish,
        };
        var segments = room.GetBoundarySegments(options);
        var loops = new List<IReadOnlyList<PlanPoint>>();
        if (segments is null)
        {
            return loops;
        }

        foreach (var loop in segments)
        {
            if (loop is null)
            {
                continue;
            }

            var points = new List<PlanPoint>();
            foreach (var segment in loop)
            {
                var curve = segment?.GetCurve();
                if (curve is null)
                {
                    continue;
                }

                AppendCurve(curve, points);
            }

            if (points.Count > 0)
            {
                loops.Add(points);
            }
        }

        return loops;
    }

    private static void AppendCurve(Curve curve, List<PlanPoint> points)
    {
        if (curve is Line)
        {
            Add(points, curve.GetEndPoint(0));
            Add(points, curve.GetEndPoint(1));
            return;
        }

        IList<XYZ> tessellated;
        try
        {
            tessellated = curve.Tessellate();
        }
        catch (Exception)
        {
            tessellated = [curve.GetEndPoint(0), curve.GetEndPoint(1)];
        }

        if (tessellated.Count == 0)
        {
            Add(points, curve.GetEndPoint(0));
            Add(points, curve.GetEndPoint(1));
            return;
        }

        foreach (var point in Decimate(tessellated))
        {
            Add(points, point);
        }
    }

    private static IEnumerable<XYZ> Decimate(IList<XYZ> points)
    {
        XYZ? kept = null;
        foreach (var point in points)
        {
            if (kept is null || DistanceMetres(kept, point) >= RoomLayoutLimits.ChordToleranceMetres)
            {
                kept = point;
                yield return point;
            }
        }

        var last = points[points.Count - 1];
        if (kept is not null && DistanceMetres(kept, last) > 1e-6)
        {
            yield return last;
        }
    }

    private static void Add(List<PlanPoint> points, XYZ point)
    {
        points.Add(new PlanPoint(ToMetres(point.X), ToMetres(point.Y)));
    }

    private static double ToMetres(double feet)
    {
        return UnitUtils.ConvertFromInternalUnits(feet, UnitTypeId.Meters);
    }

    private static double DistanceMetres(XYZ left, XYZ right)
    {
        var dx = ToMetres(left.X - right.X);
        var dy = ToMetres(left.Y - right.Y);
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    private static List<FamilyInstance> CollectOpenings(Document document, out bool truncated)
    {
        var openings = new List<FamilyInstance>();
        AppendCategory(document, BuiltInCategory.OST_Doors, openings);
        AppendCategory(document, BuiltInCategory.OST_Windows, openings);
        truncated = openings.Count >= RoomLayoutLimits.MaxOpeningScan;
        return openings;
    }

    private static void AppendCategory(Document document, BuiltInCategory category, List<FamilyInstance> openings)
    {
        if (openings.Count >= RoomLayoutLimits.MaxOpeningScan)
        {
            return;
        }

        var collector = new FilteredElementCollector(document)
            .OfCategory(category)
            .WhereElementIsNotElementType();
        foreach (var element in collector)
        {
            if (element is FamilyInstance instance)
            {
                openings.Add(instance);
            }

            if (openings.Count >= RoomLayoutLimits.MaxOpeningScan)
            {
                return;
            }
        }
    }

    private static IReadOnlyList<GetRoomLayoutFeature> ReadFeatures(
        Room room,
        IReadOnlyList<FamilyInstance> openings,
        out bool truncated)
    {
        var features = new List<GetRoomLayoutFeature>();
        var matched = 0;
        foreach (var opening in openings)
        {
            if (!Touches(opening, room))
            {
                continue;
            }

            matched++;
            if (features.Count >= RoomLayoutLimits.MaxFeaturesPerRoom)
            {
                continue;
            }

            if (TryFeature(opening, out var feature))
            {
                features.Add(feature);
            }
        }

        truncated = matched > features.Count;
        return features;
    }

    private static bool Touches(FamilyInstance opening, Room room)
    {
        try
        {
            if (Same(opening.FromRoom, room) || Same(opening.ToRoom, room))
            {
                return true;
            }

            if (room.Document.GetElement(room.CreatedPhaseId) is Phase phase)
            {
                return Same(opening.get_FromRoom(phase), room) || Same(opening.get_ToRoom(phase), room);
            }
        }
        catch (Exception)
        {
            return false;
        }

        return false;
    }

    private static bool Same(Room? candidate, Room room)
    {
        return candidate is not null && candidate.Id == room.Id;
    }

    private static bool TryFeature(FamilyInstance opening, out GetRoomLayoutFeature feature)
    {
        feature = null!;
        if (opening.Location is not LocationPoint location)
        {
            return false;
        }

        var kind = opening.Category?.BuiltInCategory switch
        {
            BuiltInCategory.OST_Doors => RoomFeatureKind.Door,
            BuiltInCategory.OST_Windows => RoomFeatureKind.Window,
            _ => (RoomFeatureKind?)null,
        };
        if (kind is null)
        {
            return false;
        }

        var facing = opening.FacingOrientation;
        var facingLength = Math.Sqrt((facing.X * facing.X) + (facing.Y * facing.Y));
        double? facingX = null;
        double? facingY = null;
        if (facingLength > 1e-6)
        {
            facingX = RoomLayoutGeometry.Round(facing.X / facingLength);
            facingY = RoomLayoutGeometry.Round(facing.Y / facingLength);
        }

        feature = new GetRoomLayoutFeature
        {
            Kind = kind.Value,
            XMetres = RoomLayoutGeometry.Round(ToMetres(location.Point.X)),
            YMetres = RoomLayoutGeometry.Round(ToMetres(location.Point.Y)),
            FacingX = facingX,
            FacingY = facingY,
            WidthMetres = WidthMetres(opening),
        };
        return true;
    }

    private static double? WidthMetres(FamilyInstance opening)
    {
        var parameter = Parameter(opening, BuiltInParameter.DOOR_WIDTH)
            ?? Parameter(opening, BuiltInParameter.WINDOW_WIDTH)
            ?? Parameter(opening, BuiltInParameter.FAMILY_WIDTH_PARAM)
            ?? Parameter(opening.Symbol, BuiltInParameter.DOOR_WIDTH)
            ?? Parameter(opening.Symbol, BuiltInParameter.WINDOW_WIDTH)
            ?? Parameter(opening.Symbol, BuiltInParameter.FAMILY_WIDTH_PARAM);
        if (parameter is null || parameter.StorageType != StorageType.Double || !parameter.HasValue)
        {
            return null;
        }

        var metres = ToMetres(parameter.AsDouble());
        if (!double.IsFinite(metres) || metres <= 0 || metres > 20)
        {
            return null;
        }

        return RoomLayoutGeometry.Round(metres);
    }

    private static Parameter? Parameter(Element? element, BuiltInParameter builtIn)
    {
        return element?.get_Parameter(builtIn);
    }

    private static string? FirstText(string? preferred, string? fallback)
    {
        return string.IsNullOrEmpty(preferred) ? fallback : preferred;
    }

    private static string? BoundText(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        var builder = new System.Text.StringBuilder(Math.Min(value.Length, RoomLayoutLimits.MaxTextLength));
        foreach (var character in value)
        {
            if (builder.Length >= RoomLayoutLimits.MaxTextLength)
            {
                break;
            }

            if (!char.IsControl(character))
            {
                builder.Append(character);
            }
        }

        return builder.Length == 0 ? null : builder.ToString();
    }

    private static GetRoomLayoutItem Rejected(string elementRef, RoomLayoutItemStatus status)
    {
        return new GetRoomLayoutItem
        {
            ElementRef = elementRef,
            Status = status,
        };
    }
}
