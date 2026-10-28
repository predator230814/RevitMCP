using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using BimIt.Revit.Addin.Execution;
using BimIt.Revit.Addin.Identity;
using BimIt.Revit.Addin.Inspection;
using BimIt.Revit.Bridge;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Addin.Capabilities;

internal sealed class RevitReadSpatialRoomsService : IRevitReadSpatialRoomsService
{
    private const int MaxLinks = 16;

    private readonly RevitExecutionDispatcher _dispatcher;
    private readonly BridgeInstanceMetadata _metadata;
    private readonly OpenDocumentIdentityService _identity;

    public RevitReadSpatialRoomsService(
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

    public async Task<ReadSpatialRoomsResult> ReadSpatialRoomsAsync(
        ReadSpatialRoomsRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var validated = ReadSpatialRoomsRequestValidator.Validate(request);
        try
        {
            return await _dispatcher.EnqueueAsync(application => Execute(application, validated), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (BridgeException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new BridgeException(CapabilityErrorCodes.ExecutionFailed, "The spatial room request could not be executed.");
        }
    }

    private ReadSpatialRoomsResult Execute(UIApplication application, ValidatedReadSpatialRoomsRequest request)
    {
        var uiDocument = application.ActiveUIDocument;
        if (uiDocument?.Document is not Document document)
        {
            throw new BridgeException(CapabilityErrorCodes.NoActiveDocument, "No active Revit document is available.");
        }

        var documentId = _identity.GetId(document);
        if (!string.Equals(request.DocumentId, documentId, StringComparison.Ordinal))
        {
            throw new BridgeException(CapabilityErrorCodes.DocumentContextChanged, "The supplied document_id does not match the active document.");
        }

        var items = new List<SpatialRoomItem>();
        var truncated = false;
        var hostOpenings = Openings(document);
        foreach (var room in Rooms(document))
        {
            if (items.Count >= request.Limit)
            {
                truncated = true;
                break;
            }

            if (!NameMatches(room, request.NameContains))
            {
                continue;
            }

            items.Add(ReadRoom(document, room, hostOpenings, SpatialPlanFrame.Identity, "host", null, SpatialRoomRefCodec.Host(room.UniqueId)));
        }

        var links = new List<SpatialLinkReport>();
        var seen = 0;
        foreach (var element in new FilteredElementCollector(document).OfClass(typeof(RevitLinkInstance)))
        {
            if (element is not RevitLinkInstance link)
            {
                continue;
            }

            seen++;
            if (seen > MaxLinks)
            {
                truncated = true;
                break;
            }

            var label = SpatialLinkLabel.Sanitize(link.Name);
            var linked = link.GetLinkDocument();
            if (linked is null)
            {
                links.Add(new SpatialLinkReport { Label = label, Status = "unavailable" });
                continue;
            }

            links.Add(new SpatialLinkReport { Label = label, Status = "loaded" });
            if (items.Count >= request.Limit)
            {
                truncated = true;
                continue;
            }

            var frame = Frame(link.GetTotalTransform());
            var openings = Openings(linked);
            foreach (var room in Rooms(linked))
            {
                if (items.Count >= request.Limit)
                {
                    truncated = true;
                    break;
                }

                if (!NameMatches(room, request.NameContains) || string.IsNullOrEmpty(room.UniqueId) || string.IsNullOrEmpty(link.UniqueId))
                {
                    continue;
                }

                items.Add(ReadRoom(linked, room, openings, frame, "link", label, SpatialRoomRefCodec.Link(link.UniqueId, room.UniqueId)));
            }
        }

        return new ReadSpatialRoomsResult
        {
            Context = new GetRoomLayoutsContext { InstanceId = _metadata.InstanceId, DocumentId = documentId },
            LengthUnit = GetRoomLayoutsResult.Metres,
            ChordToleranceMetres = RoomLayoutLimits.ChordToleranceMetres,
            Truncated = truncated,
            Links = links,
            Items = items,
        };
    }

    private static SpatialRoomItem ReadRoom(
        Document document,
        Room room,
        IReadOnlyList<FamilyInstance> openings,
        SpatialPlanFrame frame,
        string source,
        string? linkLabel,
        string spatialRef)
    {
        var loops = ReadLoops(room, frame);
        if (loops.Count == 0 || !TryCentroid(room, frame, loops[0], out var centroidX, out var centroidY))
        {
            return Unavailable(spatialRef, source, linkLabel, RoomLayoutItemStatus.BoundaryUnavailable, room, document);
        }

        return new SpatialRoomItem
        {
            SpatialRoomRef = spatialRef,
            Source = source,
            LinkLabel = linkLabel,
            Status = RoomLayoutItemStatus.Ok,
            Name = Text(room, BuiltInParameter.ROOM_NAME),
            Number = Text(room, BuiltInParameter.ROOM_NUMBER),
            LevelName = document.GetElement(room.LevelId) is Level level ? Bound(level.Name) : null,
            CentroidXMetres = centroidX,
            CentroidYMetres = centroidY,
            LoopsTruncated = loops.Count > RoomLayoutLimits.MaxLoops,
            FeaturesTruncated = false,
            Loops = loops.Take(RoomLayoutLimits.MaxLoops).Select(loop => new GetRoomLayoutLoop
            {
                VerticesTruncated = loop.Count > RoomLayoutLimits.MaxVerticesPerLoop,
                Vertices = loop.Take(RoomLayoutLimits.MaxVerticesPerLoop).Select(point => new GetRoomLayoutVertex { XMetres = point.X, YMetres = point.Y }).ToArray(),
            }).ToArray(),
            Features = Features(room, openings, frame),
        };
    }

    private static SpatialRoomItem Unavailable(string spatialRef, string source, string? linkLabel, RoomLayoutItemStatus status, Room room, Document document)
    {
        return new SpatialRoomItem
        {
            SpatialRoomRef = spatialRef,
            Source = source,
            LinkLabel = linkLabel,
            Status = status,
            Name = Text(room, BuiltInParameter.ROOM_NAME),
            Number = Text(room, BuiltInParameter.ROOM_NUMBER),
            LevelName = document.GetElement(room.LevelId) is Level level ? Bound(level.Name) : null,
        };
    }

    private static IEnumerable<Room> Rooms(Document document)
    {
        foreach (var element in new FilteredElementCollector(document).OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType())
        {
            if (element is Room room && room.Location is LocationPoint && room.Area > 0)
            {
                yield return room;
            }
        }
    }

    private static List<FamilyInstance> Openings(Document document)
    {
        var openings = new List<FamilyInstance>();
        Append(document, BuiltInCategory.OST_Doors, openings);
        Append(document, BuiltInCategory.OST_Windows, openings);
        return openings;
    }

    private static void Append(Document document, BuiltInCategory category, List<FamilyInstance> openings)
    {
        if (openings.Count >= RoomLayoutLimits.MaxOpeningScan)
        {
            return;
        }

        foreach (var element in new FilteredElementCollector(document).OfCategory(category).WhereElementIsNotElementType())
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

    private static IReadOnlyList<GetRoomLayoutFeature> Features(Room room, IReadOnlyList<FamilyInstance> openings, SpatialPlanFrame frame)
    {
        var features = new List<GetRoomLayoutFeature>();
        foreach (var opening in openings)
        {
            if (features.Count >= RoomLayoutLimits.MaxFeaturesPerRoom || !Touches(opening, room) || opening.Location is not LocationPoint location)
            {
                continue;
            }

            var point = frame.Apply(Metres(location.Point.X), Metres(location.Point.Y));
            var facing = frame.ApplyVector(opening.FacingOrientation.X, opening.FacingOrientation.Y);
            var length = Math.Sqrt((facing.X * facing.X) + (facing.Y * facing.Y));
            var kind = opening.Category?.BuiltInCategory == BuiltInCategory.OST_Doors ? RoomFeatureKind.Door : RoomFeatureKind.Window;
            var other = OtherSide(opening, room);
            features.Add(new GetRoomLayoutFeature
            {
                Kind = kind,
                XMetres = Round(point.X),
                YMetres = Round(point.Y),
                FacingX = length > 1e-6 ? Round(facing.X / length) : null,
                FacingY = length > 1e-6 ? Round(facing.Y / length) : null,
                WidthMetres = WidthMetres(opening),
                OtherSideName = other.Name,
                OtherSideNumber = other.Number,
                OtherSideStatus = other.Status,
            });
        }

        return features;
    }

    internal readonly record struct OpeningSide(string? Name, string? Number, string Status);

    internal static OpeningSide DescribeSide(bool currentIsFrom, bool currentIsTo, bool otherExists, string? otherName, string? otherNumber)
    {
        if (!currentIsFrom && !currentIsTo)
        {
            return new OpeningSide(null, null, "unknown");
        }

        if (!otherExists)
        {
            return new OpeningSide(null, null, "outside");
        }

        if (Unsafe(otherName) || Unsafe(otherNumber))
        {
            return new OpeningSide(null, null, "unknown");
        }

        return new OpeningSide(otherName, otherNumber, "room");
    }

    private static OpeningSide OtherSide(FamilyInstance opening, Room room)
    {
        var phase = PhaseOf(room);
        var from = Side(opening, phase, true);
        var to = Side(opening, phase, false);
        var currentIsFrom = from?.UniqueId == room.UniqueId;
        var currentIsTo = to?.UniqueId == room.UniqueId;
        Room? other = currentIsFrom ? to : currentIsTo ? from : null;
        return DescribeSide(
            currentIsFrom,
            currentIsTo,
            other is not null,
            other is null ? null : Text(other, BuiltInParameter.ROOM_NAME),
            other is null ? null : Text(other, BuiltInParameter.ROOM_NUMBER));
    }

    private static Phase? PhaseOf(Room room)
    {
        try
        {
            return room.Document.GetElement(room.CreatedPhaseId) as Phase;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static Room? Side(FamilyInstance opening, Phase? phase, bool from)
    {
        try
        {
            if (phase is not null)
            {
                return from ? opening.get_FromRoom(phase) : opening.get_ToRoom(phase);
            }

            return from ? opening.FromRoom : opening.ToRoom;
        }
        catch (Exception)
        {
            return null;
        }
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

        var metres = Metres(parameter.AsDouble());
        return double.IsFinite(metres) && metres > 0 && metres <= 20 ? Round(metres) : null;
    }

    private static Parameter? Parameter(Element? element, BuiltInParameter builtIn)
    {
        return element?.get_Parameter(builtIn);
    }

    private static bool Unsafe(string? value)
    {
        return value is not null
            && (value.Contains('\\', StringComparison.Ordinal)
                || value.Contains('/', StringComparison.Ordinal)
                || value.Contains(':', StringComparison.Ordinal)
                || value.Contains(".rvt", StringComparison.OrdinalIgnoreCase));
    }

    private static bool Touches(FamilyInstance opening, Room room)
    {
        try
        {
            return opening.FromRoom?.UniqueId == room.UniqueId || opening.ToRoom?.UniqueId == room.UniqueId;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static List<IReadOnlyList<(double X, double Y)>> ReadLoops(Room room, SpatialPlanFrame frame)
    {
        var result = new List<IReadOnlyList<(double X, double Y)>>();
        var segments = room.GetBoundarySegments(new SpatialElementBoundaryOptions { SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish });
        if (segments is null)
        {
            return result;
        }

        foreach (var loop in segments)
        {
            if (loop is null)
            {
                continue;
            }

            var points = new List<(double X, double Y)>();
            foreach (var segment in loop)
            {
                var curve = segment?.GetCurve();
                if (curve is null)
                {
                    continue;
                }

                Add(points, frame, curve.GetEndPoint(0));
                Add(points, frame, curve.GetEndPoint(1));
            }

            if (points.Count >= 3)
            {
                result.Add(points);
            }
        }

        return result;
    }

    private static void Add(List<(double X, double Y)> points, SpatialPlanFrame frame, XYZ point)
    {
        var host = frame.Apply(Metres(point.X), Metres(point.Y));
        var rounded = (Round(host.X), Round(host.Y));
        if (points.Count == 0 || points[^1] != rounded)
        {
            points.Add(rounded);
        }
    }

    private static bool TryCentroid(Room room, SpatialPlanFrame frame, IReadOnlyList<(double X, double Y)> loop, out double x, out double y)
    {
        if (room.Location is LocationPoint location)
        {
            var host = frame.Apply(Metres(location.Point.X), Metres(location.Point.Y));
            x = Round(host.X);
            y = Round(host.Y);
            return true;
        }

        double twice = 0;
        double cx = 0;
        double cy = 0;
        for (var index = 0; index < loop.Count; index++)
        {
            var current = loop[index];
            var next = loop[(index + 1) % loop.Count];
            var cross = (current.X * next.Y) - (next.X * current.Y);
            twice += cross;
            cx += (current.X + next.X) * cross;
            cy += (current.Y + next.Y) * cross;
        }

        if (Math.Abs(twice) < 1e-8)
        {
            x = 0;
            y = 0;
            return false;
        }

        x = Round(cx / (3 * twice));
        y = Round(cy / (3 * twice));
        return true;
    }

    private static SpatialPlanFrame Frame(Transform transform)
    {
        return new SpatialPlanFrame(
            Metres(transform.Origin.X),
            Metres(transform.Origin.Y),
            transform.BasisX.X,
            transform.BasisX.Y,
            transform.BasisY.X,
            transform.BasisY.Y);
    }

    private static bool NameMatches(Room room, string? filter)
    {
        if (string.IsNullOrEmpty(filter))
        {
            return true;
        }

        var name = room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? "";
        return name.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    private static string? Text(Room room, BuiltInParameter parameter)
    {
        return Bound(room.get_Parameter(parameter)?.AsString());
    }

    private static string? Bound(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length > RoomLayoutLimits.MaxTextLength ? trimmed[..RoomLayoutLimits.MaxTextLength] : trimmed;
    }

    private static double Metres(double feet)
    {
        return UnitUtils.ConvertFromInternalUnits(feet, UnitTypeId.Meters);
    }

    private static double Round(double value)
    {
        return Math.Round(value, 4, MidpointRounding.AwayFromZero);
    }
}
