using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace RevitMCP.Server;

internal static class GetElementsToolRegistration
{
    public static IMcpServerBuilder WithGetElementsTool(this IMcpServerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSingleton<McpServerTool>(Create);
        return builder;
    }

    public static McpServerTool Create(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return Create(services.GetRequiredService<GetElementsMcpTools>(), services);
    }

    public static McpServerTool Create(GetElementsMcpTools tools, IServiceProvider? services = null)
    {
        ArgumentNullException.ThrowIfNull(tools);
        var tool = McpServerTool.Create(
            tools.GetElementsAsync,
            new McpServerToolCreateOptions
            {
                Name = GetElementsToolMetadata.Name,
                Title = GetElementsToolMetadata.Title,
                Description = GetElementsToolMetadata.Description,
                ReadOnly = true,
                OpenWorld = false,
                UseStructuredContent = true,
                OutputSchema = Cap0003JsonSchemas.Output,
                SerializerOptions = McpJson.Options,
                Services = services
            });
        tool.ProtocolTool.InputSchema = Cap0003JsonSchemas.Input;
        return StrictInputMcpServerTool.Wrap(tool);
    }
}
