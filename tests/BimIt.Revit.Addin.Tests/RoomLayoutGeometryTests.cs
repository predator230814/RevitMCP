using BimIt.Revit.Addin.Inspection;
using Xunit;

namespace BimIt.Revit.Addin.Tests;

public sealed class RoomLayoutGeometryTests
{
    [Fact]
    public void Centroid_uses_the_largest_loop_in_metres()
    {
        Assert.True(RoomLayoutGeometry.TryBuildPlan([Square(0, 0, 1), Square(10, 20, 4)], out var plan));
        Assert.Equal(12, plan.Centroid.X, 4);
        Assert.Equal(22, plan.Centroid.Y, 4);
        Assert.Equal(2, plan.Loops.Count);
        Assert.False(plan.LoopsTruncated);
    }

    [Fact]
    public void Loop_and_vertex_caps_are_reported()
    {
        var loops = Enumerable.Range(0, 5).Select(index => Square(index * 10, 0, 4)).ToArray();
        Assert.True(RoomLayoutGeometry.TryBuildPlan(loops, out var plan));
        Assert.True(plan.LoopsTruncated);
        Assert.Equal(RoomLayoutLimits.MaxLoops, plan.Loops.Count);

        var dense = new List<PlanPoint>();
        for (var index = 0; index < 60; index++)
        {
            dense.Add(new PlanPoint(index * 0.2, index % 2 == 0 ? 0 : 0.2));
        }

        dense.Add(new PlanPoint(0, 4));
        Assert.True(RoomLayoutGeometry.TryBuildPlan([dense], out var densePlan));
        Assert.True(densePlan.Loops[0].VerticesTruncated);
        Assert.InRange(densePlan.Loops[0].Vertices.Count, 3, RoomLayoutLimits.MaxVerticesPerLoop);
    }

    [Fact]
    public void Tiny_loop_is_unavailable()
    {
        Assert.False(RoomLayoutGeometry.TryBuildPlan([Square(0, 0, 0.1)], out _));
    }

    [Fact]
    public void Service_reads_rooms_on_the_dispatcher_without_a_transaction_or_link()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "src", "BimIt.Revit.Addin", "Capabilities", "RevitGetRoomLayoutsService.cs"));
        Assert.Contains("EnqueueAsync", source, StringComparison.Ordinal);
        Assert.Contains("element is not Room", source, StringComparison.Ordinal);
        Assert.Contains("GetBoundarySegments", source, StringComparison.Ordinal);
        Assert.Contains("UnitTypeId.Meters", source, StringComparison.Ordinal);
        Assert.Contains("FromRoom", source, StringComparison.Ordinal);
        Assert.Contains("CapabilityErrorCodes.DocumentContextChanged", source, StringComparison.Ordinal);
        Assert.Contains("CapabilityErrorCodes.NoActiveDocument", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new Transaction", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new SubTransaction", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new TransactionGroup", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetLinkDocument", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PathName", source, StringComparison.Ordinal);
    }

    private static IReadOnlyList<PlanPoint> Square(double x, double y, double size)
    {
        return
        [
            new PlanPoint(x, y),
            new PlanPoint(x + size, y),
            new PlanPoint(x + size, y + size),
            new PlanPoint(x, y + size),
        ];
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "BimIt.Revit.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate BimIt.Revit.sln.");
    }
}
