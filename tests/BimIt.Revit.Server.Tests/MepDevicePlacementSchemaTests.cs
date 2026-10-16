using BimIt.Revit.Server;
using Xunit;

namespace BimIt.Revit.Server.Tests;

public sealed class MepDevicePlacementSchemaTests
{
    [Fact]
    public void Preview_schema_requires_project_axis_offsets_and_closed_placement_modes()
    {
        var input = PreviewMepDevicePlacementsJsonSchemas.Input.GetRawText();
        var output = PreviewMepDevicePlacementsJsonSchemas.Output.GetRawText();
        Assert.Contains("\"offset_x\"", input, StringComparison.Ordinal);
        Assert.Contains("\"offset_y\"", input, StringComparison.Ordinal);
        Assert.Contains("\"vertical_offset\"", input, StringComparison.Ordinal);
        Assert.DoesNotContain("offset_along_host", input, StringComparison.Ordinal);
        Assert.DoesNotContain("offset_along_host", output, StringComparison.Ordinal);
        Assert.Contains("level_based", output, StringComparison.Ordinal);
        Assert.Contains("wall_hosted", output, StringComparison.Ordinal);
        Assert.Contains("work_plane_based_level", output, StringComparison.Ordinal);
        Assert.Contains("work_plane_based_face", output, StringComparison.Ordinal);
        Assert.Contains("work_plane_based_other", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Server_sources_do_not_add_a_generic_creation_tool()
    {
        var directory = Path.Combine(FindRepoRoot(), "src", "BimIt.Revit.Server");
        var text = string.Concat(Directory.GetFiles(directory, "*.cs").Select(File.ReadAllText));
        Assert.DoesNotContain("create_family_instance", text, StringComparison.Ordinal);
        Assert.DoesNotContain("revit_place_level_device", text, StringComparison.Ordinal);
        Assert.DoesNotContain("revit_place_wall_device", text, StringComparison.Ordinal);
        Assert.DoesNotContain("revit_place_workplane_device", text, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
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

        throw new InvalidOperationException("Repository root was not found.");
    }
}
