using ModelContextProtocol.Client;
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
        var tool = Assert.Single(tools);
        Assert.Equal(GetContextToolMetadata.Name, tool.Name);
        Assert.Equal(GetContextToolMetadata.Title, tool.Title);
        Assert.Equal(GetContextToolMetadata.Description, tool.Description);
        Assert.NotNull(tool.ProtocolTool.Annotations);
        Assert.True(tool.ProtocolTool.Annotations.ReadOnlyHint);
        Assert.False(tool.ProtocolTool.Annotations.OpenWorldHint);
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
