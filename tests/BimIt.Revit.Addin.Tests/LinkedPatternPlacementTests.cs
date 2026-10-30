using BimIt.Revit.Addin.Audit;
using BimIt.Revit.Addin.Capabilities;
using BimIt.Revit.Addin.Intents;
using BimIt.Revit.Contracts;
using Xunit;

namespace BimIt.Revit.Addin.Tests;

public sealed class LinkedPatternPlacementTests
{
    private const string LinkRef = "11111111-2222-3333-4444-555555555555-00012345";
    private const string LinkedRoomRef = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee-00054321";
    private const string Instance = "instance-a";
    private const string Document = "doc-a";

    private static readonly string RoomRef = SpatialRoomRefCodec.Link(LinkRef, LinkedRoomRef);

    private static readonly PatternLinkTransform Rotated = new(100, 50, 3, 0, 1, 0, -1, 0, 0, 0, 0, 1);

    private static readonly PatternHostLevel[] HostLevels =
    [
        new("host-level-1", "Level 1", 3),
        new("host-level-2", "Level 2", 13),
    ];

    [Fact]
    public void Link_transform_applies_translation_rotation_and_inverse()
    {
        var translated = new PatternLinkTransform(10, 20, 3, 1, 0, 0, 0, 1, 0, 0, 0, 1);
        Assert.Equal(new MepPoint(12, 23, 7), translated.Apply(2, 3, 4));
        Assert.Equal(new MepPoint(100, 52, 3), Rotated.Apply(2, 0, 0));
        Assert.Equal(new MepPoint(-1, 0, 0), Rotated.ApplyVector(0, 1, 0));
        Assert.Equal(new MepPoint(2, 3, 4), Rotated.Inverse(Rotated.Apply(2, 3, 4)));
        Assert.True(Rotated.IsPlanRigid);
        Assert.False(new PatternLinkTransform(0, 0, 0, 1, 0, 0.2, 0, 1, 0, 0, 0, 1).IsPlanRigid);
        Assert.False(new PatternLinkTransform(0, 0, 0, 1, 0, 0, 0, -1, 0, 0, 0, 1).IsPlanRigid);
    }

    [Fact]
    public void Linked_room_boundary_wall_and_level_are_transformed_into_host_space()
    {
        var room = Build(Snapshot());
        Assert.Equal(PatternRoomSourceKind.Link, room.SourceKind);
        Assert.Equal(RoomRef, room.RoomRef);
        Assert.Equal([new PatternPlanPoint(100, 50), new PatternPlanPoint(100, 60), new PatternPlanPoint(92, 60), new PatternPlanPoint(92, 50)], room.Loop);
        var south = room.Walls.Single(wall => wall.WallRef == "wall-south");
        Assert.Equal(new PatternPlanPoint(100, 50), south.Start);
        Assert.Equal(new PatternPlanPoint(100, 60), south.End);
        Assert.Equal(PatternHostLevelStatus.Resolved, room.HostLevelStatus);
        Assert.Equal("host-level-2", room.LevelRef);
        Assert.Equal(13, room.LevelElevationFeet);
        Assert.Equal(new PatternLinkedRoomSource(LinkRef, LinkedRoomRef, "Snowdon Towers Sample Architectural", Rotated), room.Link);
    }

    [Theory]
    [InlineData((int)PatternLinkedRoomLookupStatus.LinkMissing)]
    [InlineData((int)PatternLinkedRoomLookupStatus.LinkUnloaded)]
    [InlineData((int)PatternLinkedRoomLookupStatus.RoomMissing)]
    public void Missing_or_unloaded_link_and_missing_room_fail_closed(int status)
    {
        Assert.False(PatternLinkedRoomFactsBuilder.TryBuild(RoomRef, LinkRef, LinkedRoomRef, PatternLinkedRoomSnapshot.Unavailable((PatternLinkedRoomLookupStatus)status), HostLevels, out _, out var failure));
        Assert.Equal(PreviewRoomPatternItemStatus.LinkedRoomUnavailable, failure);
    }

    [Fact]
    public void Truncated_excessive_ambiguous_and_tilted_boundaries_fail_closed()
    {
        Assert.Equal(PreviewRoomPatternItemStatus.LinkedBoundaryUnsupported, Failure(Snapshot(truncated: true)));
        Assert.Equal(PreviewRoomPatternItemStatus.LinkedBoundaryUnsupported, Failure(Snapshot(extraLoops: [Square(20, 0, 9.9), Square(40, 0, 1), Square(60, 0, 1), Square(80, 0, 1)])));
        Assert.Equal(PreviewRoomPatternItemStatus.LinkedBoundaryUnsupported, Failure(Snapshot(extraLoops: [Square(20, 0, 8.6)])));
        Assert.Equal(PreviewRoomPatternItemStatus.LinkedBoundaryUnsupported, Failure(Snapshot(transform: new PatternLinkTransform(0, 0, 0, 1, 0, 0.1, 0, 1, 0, 0, 0, 1))));
        var many = Enumerable.Range(0, 33).Select(index => Segment(index * 0.1, 0, (index + 1) * 0.1, 0, null)).ToList();
        Assert.Equal(PreviewRoomPatternItemStatus.LinkedBoundaryUnsupported, Failure(Snapshot(outer: many)));
    }

    [Fact]
    public void Largest_loop_is_outer_and_smaller_loops_stay_holes()
    {
        var room = Build(Snapshot(extraLoops: [Hole()]));
        Assert.Equal(4, room.Loop.Count);
        var hole = Assert.Single(room.Holes);
        Assert.Contains(new PatternPlanPoint(97, 54), hole);
        Assert.Contains(new PatternPlanPoint(95, 56), hole);

        var studio = Build(Snapshot(extraLoops: [Hole(), Square(7, 6, 1)]));
        Assert.Equal(2, studio.Holes.Count);
    }

    [Fact]
    public void Room_facing_face_is_proven_geometrically_not_by_shell_name()
    {
        var room = Build(Snapshot());
        var south = room.Walls.Single(wall => wall.WallRef == "wall-south");
        Assert.True(south.FaceResolved);
        Assert.Equal(PatternShellSide.Exterior, south.LinkedFace!.Side);
        Assert.Equal(-1, south.LinkedFace.NormalX, 9);
        Assert.Equal(0, south.LinkedFace.NormalY, 9);
        Assert.Equal(-100, south.LinkedFace.PlaneOffset, 9);
        var north = room.Walls.Single(wall => wall.WallRef == "wall-north");
        Assert.Equal(PatternShellSide.Interior, north.LinkedFace!.Side);
    }

    [Fact]
    public void Ambiguous_missing_non_planar_and_far_faces_do_not_resolve()
    {
        var inward = new PatternLinkedFaceCandidate(PatternShellSide.Interior, true, new MepPoint(0, 1, 0), new MepPoint(5, 0, 0));
        var duplicate = inward with { Side = PatternShellSide.Exterior };
        var curved = new PatternLinkedFaceCandidate(PatternShellSide.Interior, false, default, default);
        var far = inward with { Origin = new MepPoint(5, 0.3, 0) };
        var start = new PatternPlanPoint(100, 50);
        var end = new PatternPlanPoint(100, 60);
        Assert.NotNull(PatternLinkedRoomFactsBuilder.SelectFace([inward], Rotated, start, end, counterClockwise: true));
        Assert.Null(PatternLinkedRoomFactsBuilder.SelectFace([inward, duplicate], Rotated, start, end, counterClockwise: true));
        Assert.Null(PatternLinkedRoomFactsBuilder.SelectFace([], Rotated, start, end, counterClockwise: true));
        Assert.Null(PatternLinkedRoomFactsBuilder.SelectFace([curved], Rotated, start, end, counterClockwise: true));
        Assert.Null(PatternLinkedRoomFactsBuilder.SelectFace([far], Rotated, start, end, counterClockwise: true));
        Assert.Null(PatternLinkedRoomFactsBuilder.SelectFace([inward], Rotated, start, end, counterClockwise: false));
    }

    [Fact]
    public void Curved_linked_wall_marks_the_boundary_unsupported()
    {
        var outer = Rectangle();
        outer[0] = Segment(0, 0, 10, 0, Wall("wall-south", 0, 1, PatternShellSide.Exterior), straight: false);
        var room = Build(Snapshot(outer: outer));
        Assert.False(room.BoundarySupported);
        Assert.Equal(PreviewRoomPatternItemStatus.LinkedBoundaryUnsupported, Status(Facts(room), Boundary()));
    }

    [Fact]
    public void Host_level_is_resolved_by_elevation_only()
    {
        Assert.Equal(PatternHostLevelStatus.Resolved, PatternLinkedRoomFactsBuilder.MatchHostLevel(13, HostLevels, out var level));
        Assert.Equal("host-level-2", level.LevelRef);
        Assert.Equal(PatternHostLevelStatus.NotFound, PatternLinkedRoomFactsBuilder.MatchHostLevel(14, HostLevels, out _));
        Assert.Equal(PatternHostLevelStatus.Ambiguous, PatternLinkedRoomFactsBuilder.MatchHostLevel(13, [.. HostLevels, new("host-level-2b", "Mezzanine", 13.0005)], out _));
        Assert.Equal(PatternHostLevelStatus.NotFound, PatternLinkedRoomFactsBuilder.MatchHostLevel(13, [new("named", "Level 2", 20)], out _));
        var room = Build(Snapshot(levelElevation: 30));
        Assert.Equal(PatternHostLevelStatus.NotFound, room.HostLevelStatus);
        Assert.Equal(string.Empty, room.LevelRef);
        Assert.Equal(33, room.LevelElevationFeet);
    }

    [Fact]
    public void Work_plane_boundary_on_a_linked_wall_composes_on_the_proven_face()
    {
        var item = Compose(Facts(Build(Snapshot())), Boundary());
        Assert.Equal(PatternPlacementStrategyKind.BoundaryLinkedWallFace, item.Strategy);
        Assert.Equal(new PatternPlanPoint(99.51, 55), item.Requested);
        Assert.Equal(100, item.Target.X, 9);
        Assert.Equal(55, item.Target.Y, 9);
        Assert.Equal(14, item.Target.Z, 9);
        Assert.Equal(new MepPlanDirection(-1, 0), item.ReferenceDirection);
        Assert.Equal("wall-south", item.Host!.WallRef);
        Assert.Equal(LinkRef, item.Host.LinkInstanceRef);
        Assert.Equal(PatternShellSide.Exterior, item.Host.LinkedFace!.Side);
    }

    [Fact]
    public void Linked_wall_rejects_hosted_types_and_host_rooms_keep_them()
    {
        Assert.Equal(PreviewRoomPatternItemStatus.LinkedHostRequiresWorkPlaneBased, Status(Facts(Build(Snapshot()), "one_level_based_hosted"), Boundary()));
        var outer = Rectangle();
        outer[0] = Segment(0, 0, 10, 0, new PatternLinkedWall { WallRef = "wall-south", WallName = "Wall", LocationStraight = true, Faces = [] });
        Assert.Equal(PreviewRoomPatternItemStatus.UnsupportedFace, Status(Facts(Build(Snapshot(outer: outer))), Boundary()));
    }

    [Fact]
    public void Level_based_wall_device_in_a_linked_room_stands_on_the_face_on_the_host_level()
    {
        var item = Compose(Facts(Build(Snapshot()), "one_level_based"), Boundary());
        Assert.Equal(PatternPlacementStrategyKind.BoundaryLevel, item.Strategy);
        Assert.Equal("host-level-2", item.LevelRef);
        Assert.Equal(100, item.Target.X, 9);
        Assert.Equal(55, item.Target.Y, 9);
        Assert.Equal(14, item.Target.Z, 9);
        Assert.Equal(new MepPlanDirection(-1, 0), item.ReferenceDirection);
        Assert.Equal(LinkRef, item.Host!.LinkInstanceRef);
        Assert.NotNull(item.Host.LinkedFace);
        var intent = Intent(Facts(Build(Snapshot()), "one_level_based"), Boundary());
        Assert.True(PatternMepPlacementCanonicalEncoder.TryEncode(Instance, Document, intent, out _, out var level));
        Assert.True(PatternMepPlacementCanonicalEncoder.TryEncode(Instance, Document, Intent(Facts(Build(Snapshot())), Boundary()), out _, out var face));
        Assert.NotEqual(face, level);
        Assert.Equal("Level-based unhosted wall placement", Approval.ApprovalReviewRenderer.PatternPlacementLabel(item.Strategy));

        Assert.Equal(PreviewRoomPatternItemStatus.HostLevelNotFound, Status(Facts(Build(Snapshot(levelElevation: 30)), "one_level_based"), Boundary()));
        var ambiguous = PatternLinkedRoomFactsBuilder.TryBuild(RoomRef, LinkRef, LinkedRoomRef, Snapshot(), [.. HostLevels, new("other", "Other", 13)], out var room, out _);
        Assert.True(ambiguous);
        Assert.Equal(PreviewRoomPatternItemStatus.HostLevelAmbiguous, Status(Facts(room, "one_level_based"), Boundary()));
    }

    [Fact]
    public void Level_based_wall_device_is_created_unhosted_and_verified_by_its_facing()
    {
        var intent = Intent(Facts(Build(Snapshot()), "one_level_based"), Boundary());
        var session = new FakeSession(intent);
        Assert.Equal(ApplyMutationKind.Applied, PatternPlacementBatch.Execute(intent, session).Kind);
        Assert.Contains("wall_level", session.Calls);
        Assert.Contains("face_into_room:1", session.Calls);

        AssertRolledBack(intent, new FakeSession(intent) { Mutate = placed => Copy(placed, facing: new MepPlanDirection(1, 0)) });
        AssertRolledBack(intent, new FakeSession(intent) { Mutate = placed => Copy(placed, level: "host-level-1") });
        AssertRolledBack(intent, new FakeSession(intent) { Mutate = placed => Copy(placed, location: placed.Location with { Z = placed.Location.Z + 0.1 }) });
    }

    [Fact]
    public void Face_devices_stand_upright_and_face_the_room_through_their_work_plane_normal()
    {
        Assert.Equal(new MepPlanDirection(0, 1), PatternPlacementBatch.FaceReferenceDirection(1, 0));
        Assert.Equal(new MepPlanDirection(-1, 0), PatternPlacementBatch.FaceReferenceDirection(0, 2));

        var intent = Intent(Facts(Build(Snapshot())), Boundary());
        var inPlaneFacing = new FakeSession(intent) { Mutate = placed => Copy(placed, facing: new MepPlanDirection(0, -1)) };
        Assert.Equal(ApplyMutationKind.Applied, PatternPlacementBatch.Execute(intent, inPlaneFacing).Kind);

        var outward = new FakeSession(intent) { Mutate = placed => Copy(placed, workPlaneNormal: new MepPlanDirection(1, 0)) };
        AssertRolledBack(intent, outward);
        var missing = new FakeSession(intent) { Mutate = placed => Copy(placed, workPlaneNormal: new MepPlanDirection(0, 0)) };
        AssertRolledBack(intent, missing);
    }

    [Fact]
    public void Linked_boundary_ignores_nearby_edges_without_a_wall_but_not_a_second_wall()
    {
        var withoutEastWall = Rectangle();
        withoutEastWall[1] = Segment(10, 0, 10, 8, null);
        var item = Compose(Facts(Build(Snapshot(outer: withoutEastWall))), Boundary(99.51, 59.9));
        Assert.Equal(PatternPlacementStrategyKind.BoundaryLinkedWallFace, item.Strategy);
        Assert.Equal("wall-south", item.Host!.WallRef);

        Assert.Equal(PreviewRoomPatternItemStatus.AmbiguousWall, Status(Facts(Build(Snapshot())), Boundary(99.51, 59.9)));
    }

    [Fact]
    public void Linked_interior_needs_a_unique_host_level()
    {
        var resolved = Compose(Facts(Build(Snapshot()), "one_level_based"), Interior(98, 52));
        Assert.Equal(PatternPlacementStrategyKind.InteriorLevel, resolved.Strategy);
        Assert.Equal("host-level-2", resolved.LevelRef);
        var plane = Compose(Facts(Build(Snapshot()), "work_plane_based"), Interior(98, 52, heightFeet: 0));
        Assert.Equal(PatternPlacementStrategyKind.InteriorLevelWorkPlane, plane.Strategy);

        Assert.Equal(PreviewRoomPatternItemStatus.HostLevelNotFound, Status(Facts(Build(Snapshot(levelElevation: 30)), "one_level_based"), Interior(98, 52)));
        var ambiguous = PatternLinkedRoomFactsBuilder.TryBuild(RoomRef, LinkRef, LinkedRoomRef, Snapshot(), [.. HostLevels, new("other", "Other", 13)], out var room, out _);
        Assert.True(ambiguous);
        Assert.Equal(PreviewRoomPatternItemStatus.HostLevelAmbiguous, Status(Facts(room, "one_level_based"), Interior(98, 52)));
        Assert.Equal(PatternPlacementStrategyKind.BoundaryLinkedWallFace, Compose(Facts(Build(Snapshot(levelElevation: 30))), Boundary()).Strategy);
    }

    [Fact]
    public void Targets_inside_a_hole_or_outside_the_outer_loop_are_rejected()
    {
        var facts = Facts(Build(Snapshot(extraLoops: [Hole()])), "one_level_based");
        Assert.Equal(PreviewRoomPatternItemStatus.OutsideRoom, Status(facts, Interior(96, 55)));
        Assert.Equal(PreviewRoomPatternItemStatus.OutsideRoom, Status(facts, Interior(110, 55)));
        Assert.Equal(PatternPlacementStrategyKind.InteriorLevel, Compose(facts, Interior(98, 52)).Strategy);
    }

    [Fact]
    public void Fabricated_or_unavailable_linked_refs_and_mixed_batches_create_no_intent()
    {
        var facts = Facts(Build(Snapshot()));
        Assert.Equal(PreviewRoomPatternItemStatus.LinkedRoomUnavailable, Status(facts, Boundary(roomRef: "sr1.l.not-a-room")));
        var failed = new PatternProjectFacts
        {
            Types = facts.Types,
            RoomFailures = new Dictionary<string, PreviewRoomPatternItemStatus>(StringComparer.Ordinal) { [RoomRef] = PreviewRoomPatternItemStatus.LinkedBoundaryUnsupported },
        };
        Assert.Equal(PreviewRoomPatternItemStatus.LinkedBoundaryUnsupported, Status(failed, Boundary()));

        Assert.False(PatternPlacementComposer.TryCompose([Boundary(), Interior(110, 55)], facts, out var intent, out var statuses));
        Assert.Null(intent);
        Assert.Equal([PreviewRoomPatternItemStatus.Ok, PreviewRoomPatternItemStatus.OutsideRoom], statuses);
    }

    [Fact]
    public void Occupancy_stays_a_host_check_on_the_projected_target()
    {
        var facts = Facts(Build(Snapshot()), occupants: [new PatternOccupant("symbol-1", new MepPoint(100, 55, 14))]);
        Assert.Equal(PreviewRoomPatternItemStatus.Occupied, Status(facts, Boundary()));
    }

    [Fact]
    public void Linked_codec_resolves_only_well_formed_refs()
    {
        Assert.True(SpatialRoomRefCodec.TryLink(RoomRef, out var link, out var room));
        Assert.Equal(LinkRef, link);
        Assert.Equal(LinkedRoomRef, room);
        Assert.False(SpatialRoomRefCodec.TryLink("sr1.l.only-one", out _, out _));
        Assert.False(SpatialRoomRefCodec.TryLink("sr1.l.a.b.c", out _, out _));
        Assert.False(SpatialRoomRefCodec.TryLink("sr1.l.a/b.c", out _, out _));
        Assert.False(SpatialRoomRefCodec.TryLink(SpatialRoomRefCodec.Host("room"), out _, out _));
    }

    [Fact]
    public void Fingerprint_is_deterministic_and_binds_every_linked_fact()
    {
        Assert.Equal(2, PatternMepPlacementCanonicalEncoder.SchemaVersion);
        Assert.Equal("pattern_mep_placements", PatternMepPlacementCanonicalEncoder.Domain);
        var baseline = Fingerprint(Snapshot());
        Assert.Equal(baseline, Fingerprint(Snapshot()));
        Assert.NotEqual(baseline, Fingerprint(Snapshot(transform: Rotated with { OriginX = 100.01 })));
        Assert.NotEqual(baseline, Fingerprint(Snapshot(extraLoops: [Hole()])));
        Assert.NotEqual(baseline, Fingerprint(Snapshot(levelElevation: 30)));
        Assert.NotEqual(baseline, Fingerprint(Snapshot(), hostLevels: [new("host-level-2-copy", "Level 2", 13)]));
        Assert.NotEqual(baseline, Fingerprint(Snapshot(linkLabel: "Other link")));
        var otherWall = Rectangle();
        otherWall[0] = Segment(0, 0, 10, 0, Wall("wall-south-new", 0, 1, PatternShellSide.Exterior));
        Assert.NotEqual(baseline, Fingerprint(Snapshot(outer: otherWall)));
        var otherSide = Rectangle();
        otherSide[0] = Segment(0, 0, 10, 0, Wall("wall-south", 0, 1, PatternShellSide.Interior));
        Assert.NotEqual(baseline, Fingerprint(Snapshot(outer: otherSide)));
        Assert.NotEqual(baseline, Fingerprint(Snapshot(), linkedRoom: "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee-00099999"));
    }

    [Fact]
    public void Revalidation_accepts_the_same_truth_and_rejects_every_changed_fact()
    {
        var intent = Intent(Facts(Build(Snapshot())), Boundary(), Interior(98, 52, "symbol-2"));
        Assert.True(PatternMepPlacementCanonicalEncoder.TryEncode(Instance, Document, intent, out _, out var stored));
        var drafts = PatternPlacementRevalidation.Drafts(intent);
        Assert.True(PatternPlacementRevalidation.Same(Instance, Document, stored, drafts, Facts(Build(Snapshot())), out var fresh));
        Assert.Equal(2, fresh!.Items.Count);

        Assert.False(Same(stored, drafts, Facts(Build(Snapshot(transform: Rotated with { OriginY = 50.5 })))));
        Assert.False(Same(stored, drafts, new PatternProjectFacts
        {
            Types = Facts(Build(Snapshot())).Types,
            RoomFailures = new Dictionary<string, PreviewRoomPatternItemStatus>(StringComparer.Ordinal) { [RoomRef] = PreviewRoomPatternItemStatus.LinkedRoomUnavailable },
        }));
        var changedWall = Rectangle();
        changedWall[0] = Segment(0, 0, 10, 0, Wall("wall-south-replaced", 0, 1, PatternShellSide.Exterior));
        Assert.False(Same(stored, drafts, Facts(Build(Snapshot(outer: changedWall)))));
        Assert.False(Same(stored, drafts, Facts(Build(Snapshot(extraLoops: [Square(7, 6, 1)])))));
        Assert.False(Same(stored, drafts, Facts(Build(Snapshot(levelElevation: 10.5)))));
        Assert.False(Same(stored, drafts, Facts(Build(Snapshot(), [new("host-level-2-replaced", "Level 2", 13)]))));
        Assert.False(Same(stored, drafts, Facts(Build(Snapshot()), "one_level_based_hosted")));
        Assert.False(Same(stored, drafts, Facts(Build(Snapshot()), occupants: [new PatternOccupant("symbol-1", new MepPoint(100, 55, 14))])));
        Assert.False(Same(stored, drafts, new PatternProjectFacts { DocumentMatches = false }));
    }

    [Fact]
    public void Linked_face_batch_uses_the_linked_reference_path_in_one_transaction()
    {
        var intent = Intent(Facts(Build(Snapshot())), Boundary(), Interior(98, 52, "symbol-2"));
        var session = new FakeSession(intent);
        var result = PatternPlacementBatch.Execute(intent, session);
        Assert.Equal(ApplyMutationKind.Applied, result.Kind);
        Assert.Equal(AuditTransactionStatus.Committed, result.TransactionStatus);
        Assert.Equal(["created-1", "created-2"], result.CreatedElementRefs);
        Assert.Equal(["start", "activate", "linked_face", "activate", "level", "regenerate", "face_into_room:1", "regenerate", "read", "read", "commit"], session.Calls);
    }

    [Fact]
    public void Any_failure_before_commit_rolls_back_the_whole_batch()
    {
        var intent = Intent(Facts(Build(Snapshot())), Boundary(), Interior(98, 52, "symbol-2"));

        var create = new FakeSession(intent) { FailCreate = 2 };
        Assert.Equal(AuditTransactionStatus.RolledBack, PatternPlacementBatch.Execute(intent, create).TransactionStatus);
        Assert.DoesNotContain("commit", create.Calls);
        Assert.Contains("rollback", create.Calls);

        var wrongWall = new FakeSession(intent) { Mutate = placed => placed.HostFaceLinkedElementRef == "wall-south" ? Copy(placed, linkedElement: "wall-north") : placed };
        AssertRolledBack(intent, wrongWall);

        var wrongLink = new FakeSession(intent) { Mutate = placed => placed.HostFaceLinkInstanceRef is null ? placed : Copy(placed, link: "other-link") };
        AssertRolledBack(intent, wrongLink);

        var noHostFace = new FakeSession(intent) { Mutate = placed => placed.HostFaceLinkInstanceRef is null ? placed : Copy(placed, link: null, linkedElement: null) };
        AssertRolledBack(intent, noHostFace);

        var facingOut = new FakeSession(intent) { Mutate = placed => placed.WorkPlaneNormal is null ? placed : Copy(placed, workPlaneNormal: new MepPlanDirection(1, 0)) };
        AssertRolledBack(intent, facingOut);

        var offFace = new FakeSession(intent) { Mutate = placed => placed.HostFaceLinkInstanceRef is null ? placed : Copy(placed, location: placed.Location with { X = placed.Location.X - 0.1 }) };
        AssertRolledBack(intent, offFace);

        var wrongLevel = new FakeSession(intent) { Mutate = placed => placed.LevelRef is null ? placed : Copy(placed, level: "host-level-1") };
        AssertRolledBack(intent, wrongLevel);

        var cannotFlip = new FakeSession(intent) { FailFaceIntoRoom = true };
        AssertRolledBack(intent, cannotFlip);

        var duplicate = new FakeSession(intent) { DuplicateRefs = true };
        AssertRolledBack(intent, duplicate);
    }

    [Fact]
    public void Commit_failure_and_post_commit_exception_keep_their_existing_meaning()
    {
        var intent = Intent(Facts(Build(Snapshot())), Boundary());
        var refused = new FakeSession(intent) { CommitResult = false };
        var result = PatternPlacementBatch.Execute(intent, refused);
        Assert.Equal(ApplyMutationKind.TransactionFailed, result.Kind);
        Assert.Equal(AuditTransactionStatus.None, result.TransactionStatus);

        var thrown = new FakeSession(intent) { ThrowAfterCommit = true };
        var uncertain = PatternPlacementBatch.Execute(intent, thrown);
        Assert.Equal(ApplyMutationKind.CommittedUnverified, uncertain.Kind);
        Assert.Equal(AuditTransactionStatus.Committed, uncertain.TransactionStatus);
    }

    [Fact]
    public void Host_room_strategies_dispatch_unchanged()
    {
        var host = new PatternRoomFacts
        {
            RoomRef = "host-room",
            Label = "Bedroom",
            LevelRef = "level-1",
            LevelName = "Level 1",
            LevelElevationFeet = 0,
            BoundarySupported = true,
            Loop = [new(0, 0), new(10, 0), new(10, 8), new(0, 8)],
            Walls =
            [
                new PatternWallFacts { WallRef = "w-s", WallName = "w", Start = new(0, 0), End = new(10, 0), Straight = true, FaceResolved = true },
            ],
        };
        var hosted = Intent(Facts(host, "one_level_based_hosted"), Boundary(5, 0.1, "host-room"));
        Assert.Equal(PatternPlacementStrategyKind.BoundaryWallHosted, hosted.Items[0].Strategy);
        var session = new FakeSession(hosted);
        Assert.Equal(ApplyMutationKind.Applied, PatternPlacementBatch.Execute(hosted, session).Kind);
        Assert.Contains("wall_hosted", session.Calls);
        Assert.DoesNotContain("linked_face", session.Calls);
        Assert.True(PatternMepPlacementCanonicalEncoder.TryEncode(Instance, Document, hosted, out _, out _));
    }

    [Fact]
    public void Approval_pane_shows_linked_context_without_identities()
    {
        var intent = Intent(Facts(Build(Snapshot())), Boundary(), Interior(98, 52, "symbol-2"));
        var store = new EphemeralWriteIntentStore();
        var created = store.TryCreatePattern(Instance, Document, intent);
        Assert.Equal(IntentStoreCreateStatus.Created, created.Status);
        Assert.True(store.TryGetControlled(created.IntentRef!, out var controlled));
        Assert.True(Approval.ApprovalReviewRenderer.TryCreatePattern("session-1", controlled!.Binding, controlled.PatternMepPlacements!, out var model));
        var json = Approval.ApprovalReviewJson.SerializeRender(model);

        Assert.Contains("\"linkedRoomCount\":1", json, StringComparison.Ordinal);
        Assert.Contains("WorkPlaneBased linked wall-face placement", json, StringComparison.Ordinal);
        Assert.Contains("Snowdon Towers Sample Architectural", json, StringComparison.Ordinal);
        Assert.Contains("Studio Unit 203", json, StringComparison.Ordinal);
        foreach (var hidden in new[] { LinkRef, LinkedRoomRef, "sr1.", "wall-south", "host-level-2", "symbol-1", ".rvt", Instance, Document })
        {
            Assert.DoesNotContain(hidden, json, StringComparison.Ordinal);
        }
    }

    private static void AssertRolledBack(PatternMepPlacementIntent intent, FakeSession session)
    {
        var result = PatternPlacementBatch.Execute(intent, session);
        Assert.Equal(ApplyMutationKind.TransactionFailed, result.Kind);
        Assert.Equal(AuditTransactionStatus.RolledBack, result.TransactionStatus);
        Assert.DoesNotContain("commit", session.Calls);
        Assert.Equal(1, session.Calls.Count(call => call == "start"));
    }

    private static PatternPlacedFacts Copy(
        PatternPlacedFacts placed,
        string? link = "keep",
        string? linkedElement = "keep",
        MepPlanDirection? facing = null,
        MepPoint? location = null,
        string? level = "keep",
        MepPlanDirection? workPlaneNormal = null)
    {
        return new PatternPlacedFacts
        {
            SymbolRef = placed.SymbolRef,
            CategoryToken = placed.CategoryToken,
            LevelRef = level == "keep" ? placed.LevelRef : level,
            Location = location ?? placed.Location,
            Facing = facing ?? placed.Facing,
            WorkPlaneNormal = workPlaneNormal is { X: 0, Y: 0 } ? null : workPlaneNormal ?? placed.WorkPlaneNormal,
            HostRef = placed.HostRef,
            HostFaceLinkInstanceRef = link == "keep" ? placed.HostFaceLinkInstanceRef : link,
            HostFaceLinkedElementRef = linkedElement == "keep" ? placed.HostFaceLinkedElementRef : linkedElement,
        };
    }

    private static bool Same(string stored, IReadOnlyList<PatternPlacementDraft> drafts, PatternProjectFacts facts)
    {
        return PatternPlacementRevalidation.Same(Instance, Document, stored, drafts, facts, out _);
    }

    private static string Fingerprint(
        PatternLinkedRoomSnapshot snapshot,
        IReadOnlyList<PatternHostLevel>? hostLevels = null,
        string linkedRoom = LinkedRoomRef)
    {
        var roomRef = SpatialRoomRefCodec.Link(LinkRef, linkedRoom);
        Assert.True(PatternLinkedRoomFactsBuilder.TryBuild(roomRef, LinkRef, linkedRoom, snapshot, hostLevels ?? HostLevels, out var room, out _));
        var intent = Intent(Facts(room), Boundary(roomRef: roomRef));
        Assert.True(PatternMepPlacementCanonicalEncoder.TryEncode(Instance, Document, intent, out _, out var fingerprint));
        return fingerprint;
    }

    private static PatternMepPlacementIntent Intent(PatternProjectFacts facts, params PatternPlacementDraft[] drafts)
    {
        Assert.True(PatternPlacementComposer.TryCompose(drafts, facts, out var intent, out _));
        return intent!;
    }

    private static PatternMepPlacementItem Compose(PatternProjectFacts facts, PatternPlacementDraft draft) => Intent(facts, draft).Items[0];

    private static PreviewRoomPatternItemStatus Status(PatternProjectFacts facts, PatternPlacementDraft draft)
    {
        Assert.False(PatternPlacementComposer.TryCompose([draft], facts, out var intent, out var statuses));
        Assert.Null(intent);
        return statuses[0];
    }

    private static PreviewRoomPatternItemStatus Failure(PatternLinkedRoomSnapshot snapshot)
    {
        Assert.False(PatternLinkedRoomFactsBuilder.TryBuild(RoomRef, LinkRef, LinkedRoomRef, snapshot, HostLevels, out _, out var failure));
        return failure;
    }

    private static PatternRoomFacts Build(PatternLinkedRoomSnapshot snapshot, IReadOnlyList<PatternHostLevel>? hostLevels = null)
    {
        Assert.True(PatternLinkedRoomFactsBuilder.TryBuild(RoomRef, LinkRef, LinkedRoomRef, snapshot, hostLevels ?? HostLevels, out var room, out _));
        return room;
    }

    private static PatternPlacementDraft Boundary(double x = 99.51, double y = 55, string? roomRef = null)
    {
        return Draft(roomRef ?? RoomRef, "symbol-1", "receptacle", "boundary", x, y);
    }

    private static PatternPlacementDraft Interior(double x, double y, string symbol = "symbol-1", double heightFeet = 1)
    {
        return Draft(RoomRef, symbol, symbol == "symbol-2" ? "luminaire" : "receptacle", "interior", x, y, heightFeet);
    }

    private static PatternPlacementDraft Draft(string roomRef, string symbol, string role, string placement, double x, double y, double heightFeet = 1)
    {
        return new PatternPlacementDraft
        {
            RoomRef = roomRef,
            SymbolRef = symbol,
            Role = role,
            PlacementKind = placement,
            XFeet = x,
            YFeet = y,
            MountingHeightFeet = heightFeet,
            QuarterTurns = 0,
        };
    }

    private static PatternProjectFacts Facts(
        PatternRoomFacts room,
        string classification = "work_plane_based",
        IReadOnlyList<PatternOccupant>? occupants = null)
    {
        return new PatternProjectFacts
        {
            Rooms = [room],
            Types =
            [
                Type("symbol-1", "OST_ElectricalFixtures", classification),
                Type("symbol-2", "OST_LightingFixtures", "one_level_based"),
            ],
            Occupants = occupants ?? [],
        };
    }

    private static PatternTypeFacts Type(string symbol, string category, string classification)
    {
        return new PatternTypeFacts
        {
            SymbolRef = symbol,
            CategoryToken = category,
            CategoryName = "Category",
            FamilyName = "Family",
            TypeName = "Type",
            PlacementClassification = classification,
            Active = true,
        };
    }

    private static PatternLinkedRoomSnapshot Snapshot(
        PatternLinkTransform? transform = null,
        List<PatternLinkedSegment>? outer = null,
        IReadOnlyList<IReadOnlyList<PatternLinkedSegment>>? extraLoops = null,
        double levelElevation = 10,
        bool truncated = false,
        string linkLabel = "Snowdon Towers Sample Architectural")
    {
        var loops = new List<IReadOnlyList<PatternLinkedSegment>> { outer ?? Rectangle() };
        loops.AddRange(extraLoops ?? []);
        return new PatternLinkedRoomSnapshot
        {
            Status = PatternLinkedRoomLookupStatus.Ok,
            LinkLabel = linkLabel,
            Transform = transform ?? Rotated,
            Label = "Studio Unit 203",
            LevelName = "L2",
            LevelElevationFeet = levelElevation,
            BoundaryTruncated = truncated,
            Loops = loops,
        };
    }

    private static List<PatternLinkedSegment> Rectangle()
    {
        return
        [
            Segment(0, 0, 10, 0, Wall("wall-south", 0, 1, PatternShellSide.Exterior)),
            Segment(10, 0, 10, 8, Wall("wall-east", -1, 0, PatternShellSide.Interior, 10)),
            Segment(10, 8, 0, 8, Wall("wall-north", 0, -1, PatternShellSide.Interior, 8)),
            Segment(0, 8, 0, 0, Wall("wall-west", 1, 0, PatternShellSide.Interior, 0)),
        ];
    }

    private static List<PatternLinkedSegment> Hole()
    {
        return
        [
            Segment(4, 3, 4, 5, null),
            Segment(4, 5, 6, 5, null),
            Segment(6, 5, 6, 3, null),
            Segment(6, 3, 4, 3, null),
        ];
    }

    private static List<PatternLinkedSegment> Square(double x, double y, double size)
    {
        return
        [
            Segment(x, y, x + size, y, null),
            Segment(x + size, y, x + size, y + size, null),
            Segment(x + size, y + size, x, y + size, null),
            Segment(x, y + size, x, y, null),
        ];
    }

    private static PatternLinkedSegment Segment(double x1, double y1, double x2, double y2, PatternLinkedWall? wall, bool straight = true)
    {
        return new PatternLinkedSegment
        {
            Start = new MepPoint(x1, y1, 0),
            End = new MepPoint(x2, y2, 0),
            Straight = straight,
            Wall = wall,
        };
    }

    private static PatternLinkedWall Wall(string id, double nx, double ny, PatternShellSide roomSide, double line = 0)
    {
        var other = roomSide == PatternShellSide.Interior ? PatternShellSide.Exterior : PatternShellSide.Interior;
        var origin = nx != 0 ? new MepPoint(line, 4, 0) : new MepPoint(5, line, 0);
        var back = new MepPoint(origin.X - (nx * 0.5), origin.Y - (ny * 0.5), 0);
        return new PatternLinkedWall
        {
            WallRef = id,
            WallName = "Basic Wall",
            LocationStraight = true,
            Faces =
            [
                new PatternLinkedFaceCandidate(other, true, new MepPoint(-nx, -ny, 0), back),
                new PatternLinkedFaceCandidate(roomSide, true, new MepPoint(nx, ny, 0), origin),
            ],
        };
    }

    private sealed class FakeSession : IPatternPlacementSession
    {
        private readonly Dictionary<string, PatternMepPlacementItem> _created = new(StringComparer.Ordinal);
        private bool _started;
        private bool _committed;

        public FakeSession(PatternMepPlacementIntent intent)
        {
            Assert.NotEmpty(intent.Items);
        }

        public List<string> Calls { get; } = [];

        public int FailCreate { get; init; }

        public bool FailFaceIntoRoom { get; init; }

        public bool DuplicateRefs { get; init; }

        public bool CommitResult { get; init; } = true;

        public bool ThrowAfterCommit { get; init; }

        public Func<PatternPlacedFacts, PatternPlacedFacts> Mutate { get; init; } = placed => placed;

        public bool IsStarted => _started && !_committed;

        public bool Start()
        {
            Calls.Add("start");
            _started = true;
            return true;
        }

        public bool TryActivate(string symbolRef)
        {
            Calls.Add("activate");
            return true;
        }

        public bool TryCreateInteriorLevel(PatternMepPlacementItem item, out string createdRef) => Create("level", item, out createdRef);

        public bool TryCreateInteriorLevelWorkPlane(PatternMepPlacementItem item, out string createdRef) => Create("level_plane", item, out createdRef);

        public bool TryCreateWallHosted(PatternMepPlacementItem item, out string createdRef) => Create("wall_hosted", item, out createdRef);

        public bool TryCreateWallFace(PatternMepPlacementItem item, out string createdRef) => Create("wall_face", item, out createdRef);

        public bool TryCreateLinkedWallFace(PatternMepPlacementItem item, out string createdRef) => Create("linked_face", item, out createdRef);

        public bool TryCreateWallLevel(PatternMepPlacementItem item, out string createdRef) => Create("wall_level", item, out createdRef);

        public void Regenerate()
        {
            Calls.Add("regenerate");
        }

        public bool TryFaceIntoRoom(PatternMepPlacementItem item, string createdRef)
        {
            Calls.Add("face_into_room:" + item.Position);
            return !FailFaceIntoRoom;
        }

        public bool TryRead(string createdRef, out PatternPlacedFacts placed)
        {
            Calls.Add("read");
            var item = _created[createdRef];
            var linked = item.Strategy == PatternPlacementStrategyKind.BoundaryLinkedWallFace;
            var face = PatternPlacementBatch.IsFace(item.Strategy);
            placed = Mutate(new PatternPlacedFacts
            {
                SymbolRef = item.SymbolRef,
                CategoryToken = item.CategoryToken,
                LevelRef = linked ? null : item.LevelRef,
                Location = item.Target,
                // As in Revit, a face-based instance's FacingOrientation lies in the face; its work-plane normal faces the Room.
                Facing = item.Host is null || face ? null : item.ReferenceDirection,
                WorkPlaneNormal = face ? item.ReferenceDirection : null,
                HostRef = item.Host?.WallRef,
                HostFaceLinkInstanceRef = linked ? item.Host!.LinkInstanceRef : null,
                HostFaceLinkedElementRef = linked ? item.Host!.WallRef : null,
            });
            return true;
        }

        public bool Commit()
        {
            Calls.Add("commit");
            _committed = CommitResult;
            if (ThrowAfterCommit)
            {
                throw new InvalidOperationException("post-commit failure");
            }

            return CommitResult;
        }

        public void Rollback()
        {
            Calls.Add("rollback");
            _started = false;
        }

        public void Dispose()
        {
        }

        private bool Create(string kind, PatternMepPlacementItem item, out string createdRef)
        {
            Calls.Add(kind);
            createdRef = DuplicateRefs ? "created-1" : "created-" + item.Position;
            if (FailCreate == item.Position)
            {
                createdRef = string.Empty;
                return false;
            }

            _created[createdRef] = item;
            return true;
        }
    }
}
