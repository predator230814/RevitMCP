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
        Assert.Equal(3, tools.Count);
        Assert.Equal(
            new[] { GetContextToolMetadata.Name, GetElementsToolMetadata.Name, QueryElementsToolMetadata.Name },
            tools.Select(tool => tool.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray());
        Assert.DoesNotContain(tools, tool => tool.Name.Contains("handshake", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(tools, tool => tool.Name.Contains("revit.get_", StringComparison.Ordinal));
        Assert.DoesNotContain(tools, tool => tool.Name.Contains("revit.query_", StringComparison.Ordinal));

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
