using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using RevitMCP.Server;
using Xunit;

namespace RevitMCP.Server.Tests;

public sealed class StdioServerProcessTests
{
    [Fact]
    public async Task Built_server_exposes_revit_get_context_over_stdio()
    {
        var command = ResolveServerCommand();
        if (command is null)
        {
            Assert.Fail("RevitMCP.Server executable was not copied next to the test output.");
            return;
        }

        await using var client = await McpClient.CreateAsync(
            new StdioClientTransport(new StdioClientTransportOptions
            {
                Name = "RevitMCP.Server.Tests",
                Command = command.Value.FileName,
                Arguments = command.Value.Arguments,
                WorkingDirectory = AppContext.BaseDirectory,
                ShutdownTimeout = TimeSpan.FromSeconds(10)
            }));

        var tools = await client.ListToolsAsync();
        Assert.Equal(6, tools.Count);
        Assert.Equal(
            new[]
            {
                DescribeParametersToolMetadata.Name,
                GetContextToolMetadata.Name,
                GetElementsToolMetadata.Name,
                GetMepTopologyToolMetadata.Name,
                GetParameterValuesToolMetadata.Name,
                QueryElementsToolMetadata.Name
            },
            tools.Select(tool => tool.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray());
        Assert.Equal(
            new[]
            {
                "revit_describe_parameters",
                "revit_get_context",
                "revit_get_elements",
                "revit_get_mep_topology",
                "revit_get_parameter_values",
                "revit_query_elements"
            },
            tools.Select(tool => tool.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray());
        Assert.DoesNotContain(tools, tool => tool.Name.Contains("handshake", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(tools, tool => tool.Name.Contains("revit.get_", StringComparison.Ordinal));
        Assert.DoesNotContain(tools, tool => tool.Name.Contains("revit.query_", StringComparison.Ordinal));
        Assert.DoesNotContain(tools, tool => tool.Name.Contains("revit.describe_", StringComparison.Ordinal));

        var getContext = Assert.Single(tools, tool => tool.Name == GetContextToolMetadata.Name);
        Assert.Equal(GetContextToolMetadata.Title, getContext.Title);
        Assert.True(getContext.ProtocolTool.Annotations?.ReadOnlyHint);
        Assert.False(getContext.ProtocolTool.Annotations?.OpenWorldHint);

        var query = Assert.Single(tools, tool => tool.Name == QueryElementsToolMetadata.Name);
        Assert.Equal(QueryElementsToolMetadata.Title, query.Title);
        Assert.Equal(QueryElementsToolMetadata.Description, query.Description);
        Assert.True(query.ProtocolTool.Annotations?.ReadOnlyHint);
        Assert.False(query.ProtocolTool.Annotations?.OpenWorldHint);

        var getElements = Assert.Single(tools, tool => tool.Name == GetElementsToolMetadata.Name);
        Assert.Equal(GetElementsToolMetadata.Title, getElements.Title);
        Assert.Equal(GetElementsToolMetadata.Description, getElements.Description);
        Assert.True(getElements.ProtocolTool.Annotations?.ReadOnlyHint);
        Assert.False(getElements.ProtocolTool.Annotations?.OpenWorldHint);

        var describe = Assert.Single(tools, tool => tool.Name == DescribeParametersToolMetadata.Name);
        Assert.Equal(DescribeParametersToolMetadata.Title, describe.Title);
        Assert.Equal(DescribeParametersToolMetadata.Description, describe.Description);
        Assert.True(describe.ProtocolTool.Annotations?.ReadOnlyHint);
        Assert.False(describe.ProtocolTool.Annotations?.OpenWorldHint);

        var getParameterValues = Assert.Single(tools, tool => tool.Name == GetParameterValuesToolMetadata.Name);
        Assert.Equal(GetParameterValuesToolMetadata.Title, getParameterValues.Title);
        Assert.Equal(GetParameterValuesToolMetadata.Description, getParameterValues.Description);
        Assert.True(getParameterValues.ProtocolTool.Annotations?.ReadOnlyHint);
        Assert.False(getParameterValues.ProtocolTool.Annotations?.OpenWorldHint);

        var getMepTopology = Assert.Single(tools, tool => tool.Name == GetMepTopologyToolMetadata.Name);
        Assert.Equal(GetMepTopologyToolMetadata.Title, getMepTopology.Title);
        Assert.Equal(GetMepTopologyToolMetadata.Description, getMepTopology.Description);
        Assert.True(getMepTopology.ProtocolTool.Annotations?.ReadOnlyHint);
        Assert.False(getMepTopology.ProtocolTool.Annotations?.OpenWorldHint);

        var rejected = await client.CallToolAsync(
            GetElementsToolMetadata.Name,
            new Dictionary<string, object?>
            {
                ["document_id"] = "doc-1",
                ["element_refs"] = new[] { "ref-1" },
                ["projection"] = new Dictionary<string, object?> { ["fields"] = new[] { "name" } },
                ["unexpected"] = true
            });
        Assert.True(rejected.IsError);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(rejected.Content)).Text;
        Assert.Contains(McpToolErrorCodes.InvalidRequest, text, StringComparison.Ordinal);
        Assert.DoesNotContain("NO_REVIT_INSTANCE", text, StringComparison.Ordinal);

        var missingRequired = await client.CallToolAsync(
            GetElementsToolMetadata.Name,
            new Dictionary<string, object?>
            {
                ["element_refs"] = new[] { "ref-1" },
                ["projection"] = new Dictionary<string, object?> { ["fields"] = new[] { "name" } }
            });
        Assert.True(missingRequired.IsError);
        var missingText = Assert.IsType<TextContentBlock>(Assert.Single(missingRequired.Content)).Text;
        Assert.Contains(McpToolErrorCodes.InvalidRequest, missingText, StringComparison.Ordinal);
        Assert.DoesNotContain("NO_REVIT_INSTANCE", missingText, StringComparison.Ordinal);
        Assert.DoesNotContain("INVALID_INSPECTION", missingText, StringComparison.Ordinal);

        var malformedDescribe = await client.CallToolAsync(
            DescribeParametersToolMetadata.Name,
            new Dictionary<string, object?>
            {
                ["document_id"] = "doc-1",
                ["element_refs"] = new[] { "ref-1" },
                ["unexpected"] = true
            });
        Assert.True(malformedDescribe.IsError);
        var describeText = Assert.IsType<TextContentBlock>(Assert.Single(malformedDescribe.Content)).Text;
        Assert.Contains(McpToolErrorCodes.InvalidRequest, describeText, StringComparison.Ordinal);
        Assert.DoesNotContain("NO_REVIT_INSTANCE", describeText, StringComparison.Ordinal);
        Assert.DoesNotContain("INVALID_PARAMETER_DISCOVERY", describeText, StringComparison.Ordinal);

        var malformedValues = await client.CallToolAsync(
            GetParameterValuesToolMetadata.Name,
            new Dictionary<string, object?>
            {
                ["document_id"] = "doc-1",
                ["reads"] = new[]
                {
                    new Dictionary<string, object?>
                    {
                        ["element_ref"] = "ref-1",
                        ["parameter_ref"] = "pref-1"
                    }
                },
                ["unexpected"] = true
            });
        Assert.True(malformedValues.IsError);
        var valuesText = Assert.IsType<TextContentBlock>(Assert.Single(malformedValues.Content)).Text;
        Assert.Contains(McpToolErrorCodes.InvalidRequest, valuesText, StringComparison.Ordinal);
        Assert.DoesNotContain("NO_REVIT_INSTANCE", valuesText, StringComparison.Ordinal);
        Assert.DoesNotContain("INVALID_PARAMETER_READ", valuesText, StringComparison.Ordinal);

        var duplicatePairs = await client.CallToolAsync(
            GetParameterValuesToolMetadata.Name,
            new Dictionary<string, object?>
            {
                ["document_id"] = "doc-1",
                ["reads"] = new object[]
                {
                    new Dictionary<string, object?>
                    {
                        ["element_ref"] = "e1",
                        ["parameter_ref"] = "p1"
                    },
                    new Dictionary<string, object?>
                    {
                        ["parameter_ref"] = "p1",
                        ["element_ref"] = "e1"
                    }
                }
            });
        Assert.True(duplicatePairs.IsError);
        var duplicateText = Assert.IsType<TextContentBlock>(Assert.Single(duplicatePairs.Content)).Text;
        Assert.Contains(McpToolErrorCodes.InvalidRequest, duplicateText, StringComparison.Ordinal);
        Assert.DoesNotContain("NO_REVIT_INSTANCE", duplicateText, StringComparison.Ordinal);
        Assert.DoesNotContain("INVALID_PARAMETER_READ", duplicateText, StringComparison.Ordinal);

        var malformedTopology = await client.CallToolAsync(
            GetMepTopologyToolMetadata.Name,
            new Dictionary<string, object?>
            {
                ["document_id"] = "doc-1",
                ["seed_element_refs"] = new[] { "ref-1" },
                ["unexpected"] = true
            });
        Assert.True(malformedTopology.IsError);
        var topologyText = Assert.IsType<TextContentBlock>(Assert.Single(malformedTopology.Content)).Text;
        Assert.Contains(McpToolErrorCodes.InvalidRequest, topologyText, StringComparison.Ordinal);
        Assert.DoesNotContain("NO_REVIT_INSTANCE", topologyText, StringComparison.Ordinal);
        Assert.DoesNotContain("INVALID_MEP_TOPOLOGY", topologyText, StringComparison.Ordinal);
    }

    private static (string FileName, IList<string> Arguments)? ResolveServerCommand()
    {
        var exe = Path.Combine(AppContext.BaseDirectory, "RevitMCP.Server.exe");
        if (File.Exists(exe))
        {
            return (exe, []);
        }

        var dll = Path.Combine(AppContext.BaseDirectory, "RevitMCP.Server.dll");
        if (File.Exists(dll))
        {
            return ("dotnet", [dll]);
        }

        return null;
    }
}
