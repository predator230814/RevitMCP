using BimIt.Revit.Addin.Audit;
using BimIt.Revit.Addin.Intents;

namespace BimIt.Revit.Addin.Capabilities;

internal sealed class PatternPlacedFacts
{
    public required string SymbolRef { get; init; }

    public required string CategoryToken { get; init; }

    public string? LevelRef { get; init; }

    public required MepPoint Location { get; init; }

    public MepPlanDirection? Facing { get; init; }

    public MepPlanDirection? WorkPlaneNormal { get; init; }

    public string? HostRef { get; init; }

    public string? HostFaceLinkInstanceRef { get; init; }

    public string? HostFaceLinkedElementRef { get; init; }
}

internal interface IPatternPlacementSession : IDisposable
{
    bool IsStarted { get; }

    bool Start();

    bool TryActivate(string symbolRef);

    bool TryCreateInteriorLevel(PatternMepPlacementItem item, out string createdRef);

    bool TryCreateInteriorLevelWorkPlane(PatternMepPlacementItem item, out string createdRef);

    bool TryCreateWallHosted(PatternMepPlacementItem item, out string createdRef);

    bool TryCreateWallFace(PatternMepPlacementItem item, out string createdRef);

    bool TryCreateLinkedWallFace(PatternMepPlacementItem item, out string createdRef);

    bool TryCreateWallLevel(PatternMepPlacementItem item, out string createdRef);

    void Regenerate();

    bool TryFaceIntoRoom(PatternMepPlacementItem item, string createdRef);

    bool TryRead(string createdRef, out PatternPlacedFacts placed);

    bool Commit();

    void Rollback();
}

internal static class PatternPlacementBatch
{
    public static ApplyMutationResult Execute(PatternMepPlacementIntent intent, IPatternPlacementSession session)
    {
        ArgumentNullException.ThrowIfNull(intent);
        ArgumentNullException.ThrowIfNull(session);
        var createdRefs = new List<string>(intent.Items.Count);
        var committed = false;
        try
        {
            if (!session.Start())
            {
                return new ApplyMutationResult(ApplyMutationKind.TransactionFailed, AuditTransactionStatus.None);
            }

            foreach (var item in intent.Items)
            {
                if (!session.TryActivate(item.SymbolRef)
                    || !TryCreate(session, item, out var created)
                    || string.IsNullOrEmpty(created))
                {
                    return Rollback(session);
                }

                createdRefs.Add(created);
            }

            session.Regenerate();
            for (var index = 0; index < createdRefs.Count; index++)
            {
                var item = intent.Items[index];
                if (IsBoundary(item.Strategy) && !session.TryFaceIntoRoom(item, createdRefs[index]))
                {
                    return Rollback(session);
                }
            }

            session.Regenerate();
            if (!Verify(intent, createdRefs, session))
            {
                return Rollback(session);
            }

            committed = session.Commit();
            if (!committed)
            {
                return new ApplyMutationResult(ApplyMutationKind.TransactionFailed, AuditTransactionStatus.None);
            }

            return new ApplyMutationResult(ApplyMutationKind.Applied, AuditTransactionStatus.Committed, createdRefs);
        }
        catch (Exception)
        {
            if (session.IsStarted)
            {
                return Rollback(session);
            }

            return createdRefs.Count > 0 || committed
                ? new ApplyMutationResult(ApplyMutationKind.CommittedUnverified, AuditTransactionStatus.Committed)
                : new ApplyMutationResult(ApplyMutationKind.TransactionFailed, AuditTransactionStatus.None);
        }
    }

    internal static bool IsBoundary(PatternPlacementStrategyKind strategy)
    {
        return strategy is PatternPlacementStrategyKind.BoundaryWallHosted
            or PatternPlacementStrategyKind.BoundaryWallFace
            or PatternPlacementStrategyKind.BoundaryLinkedWallFace
            or PatternPlacementStrategyKind.BoundaryLevel;
    }

    internal static bool IsFace(PatternPlacementStrategyKind strategy)
    {
        return strategy is PatternPlacementStrategyKind.BoundaryWallFace
            or PatternPlacementStrategyKind.BoundaryLinkedWallFace;
    }

    // A work-plane-based instance on a vertical face takes its X axis from the reference direction and its Z axis from the
    // face normal. A horizontal reference direction along the face keeps the family Y axis up, so the device stands upright.
    internal static MepPlanDirection FaceReferenceDirection(double normalX, double normalY)
    {
        var length = Math.Sqrt((normalX * normalX) + (normalY * normalY));
        return length <= 1e-9
            ? new MepPlanDirection(0, 0)
            : new MepPlanDirection(-normalY / length, normalX / length);
    }

    private static bool TryCreate(IPatternPlacementSession session, PatternMepPlacementItem item, out string created)
    {
        created = string.Empty;
        return item.Strategy switch
        {
            PatternPlacementStrategyKind.InteriorLevel => session.TryCreateInteriorLevel(item, out created),
            PatternPlacementStrategyKind.InteriorLevelWorkPlane => session.TryCreateInteriorLevelWorkPlane(item, out created),
            PatternPlacementStrategyKind.BoundaryWallHosted => session.TryCreateWallHosted(item, out created),
            PatternPlacementStrategyKind.BoundaryWallFace => session.TryCreateWallFace(item, out created),
            PatternPlacementStrategyKind.BoundaryLinkedWallFace => session.TryCreateLinkedWallFace(item, out created),
            PatternPlacementStrategyKind.BoundaryLevel => session.TryCreateWallLevel(item, out created),
            _ => false,
        };
    }

    private static bool Verify(PatternMepPlacementIntent intent, IReadOnlyList<string> createdRefs, IPatternPlacementSession session)
    {
        if (createdRefs.Count != intent.Items.Count || createdRefs.Distinct(StringComparer.Ordinal).Count() != createdRefs.Count)
        {
            return false;
        }

        for (var index = 0; index < createdRefs.Count; index++)
        {
            if (!session.TryRead(createdRefs[index], out var placed) || !PatternPlacementVerifier.Verify(intent.Items[index], placed))
            {
                return false;
            }
        }

        return true;
    }

    private static ApplyMutationResult Rollback(IPatternPlacementSession session)
    {
        try
        {
            session.Rollback();
        }
        catch (Exception)
        {
        }

        return new ApplyMutationResult(ApplyMutationKind.TransactionFailed, AuditTransactionStatus.RolledBack);
    }
}

internal static class PatternPlacementVerifier
{
    public const double FacePlaneToleranceFeet = 5.0 / 304.8;

    public static bool Verify(PatternMepPlacementItem item, PatternPlacedFacts placed)
    {
        if (!string.Equals(placed.SymbolRef, item.SymbolRef, StringComparison.Ordinal)
            || !string.Equals(placed.CategoryToken, item.CategoryToken, StringComparison.Ordinal))
        {
            return false;
        }

        if (item.Strategy != PatternPlacementStrategyKind.BoundaryLinkedWallFace
            && !string.Equals(placed.LevelRef, item.LevelRef, StringComparison.Ordinal))
        {
            return false;
        }

        if (!LocationMatches(item, placed.Location))
        {
            return false;
        }

        if (item.Host is not { } host)
        {
            return true;
        }

        if (item.Strategy == PatternPlacementStrategyKind.BoundaryWallHosted
            && !string.Equals(placed.HostRef, host.WallRef, StringComparison.Ordinal))
        {
            return false;
        }

        if (item.Strategy == PatternPlacementStrategyKind.BoundaryLinkedWallFace
            && (host.LinkedFace is not { } face
                || !string.Equals(placed.HostFaceLinkInstanceRef, host.LinkInstanceRef, StringComparison.Ordinal)
                || !string.Equals(placed.HostFaceLinkedElementRef, host.WallRef, StringComparison.Ordinal)
                || Math.Abs(face.Distance(placed.Location.X, placed.Location.Y)) > FacePlaneToleranceFeet))
        {
            return false;
        }

        // A face-based instance faces the Room through its work-plane normal. Its FacingOrientation lies in the face.
        var outward = PatternPlacementBatch.IsFace(item.Strategy) ? placed.WorkPlaneNormal : placed.Facing;
        return outward is { } facing
            && (facing.X * item.ReferenceDirection.X) + (facing.Y * item.ReferenceDirection.Y) > 0.5;
    }

    private static bool LocationMatches(PatternMepPlacementItem item, MepPoint actual)
    {
        if (item.Strategy == PatternPlacementStrategyKind.BoundaryWallHosted)
        {
            return item.Host is not null
                && MepDevicePlacementGeometry.SameHostedWallPlacement(
                    actual,
                    item.Target,
                    item.Host.Start,
                    item.Host.End,
                    PatternPlacementComposer.WallToleranceFeet);
        }

        if (item.Strategy is PatternPlacementStrategyKind.BoundaryWallFace or PatternPlacementStrategyKind.BoundaryLinkedWallFace)
        {
            return MepDevicePlacementGeometry.SameWallPlacement(
                actual,
                item.Target,
                PatternPlacementComposer.WallToleranceFeet);
        }

        return MepDevicePlacementGeometry.SamePoint(actual, item.Target);
    }
}
