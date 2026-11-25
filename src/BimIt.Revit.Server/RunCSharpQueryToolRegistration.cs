using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace BimIt.Revit.Server;

internal static class RunCSharpQueryToolRegistration
{
    public static IMcpServerBuilder WithRunCSharpQueryTool(this IMcpServerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSingleton<McpServerTool>(Create);
        return builder;
    }

    public static McpServerTool Create(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return Create(services.GetRequiredService<RunCSharpQueryMcpTools>(), services);
    }

    public static McpServerTool Create(RunCSharpQueryMcpTools tools, IServiceProvider? services = null)
    {
        ArgumentNullException.ThrowIfNull(tools);
        var tool = McpServerTool.Create(
            tools.RunAsync,
            new McpServerToolCreateOptions
            {
                Name = RunCSharpQueryToolMetadata.Name,
                Title = RunCSharpQueryToolMetadata.Title,
                Description = RunCSharpQueryToolMetadata.Description,
                ReadOnly = true,
                OpenWorld = false,
                UseStructuredContent = true,
                OutputSchema = RunCSharpQueryJsonSchemas.Output,
                SerializerOptions = McpJson.Options,
                Services = services
            });
        tool.ProtocolTool.InputSchema = RunCSharpQueryJsonSchemas.Input;
        return StrictInputMcpServerTool.Wrap(tool);
    }
}
