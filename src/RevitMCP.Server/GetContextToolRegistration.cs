using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace RevitMCP.Server;

internal static class GetContextToolRegistration
{
    public static IMcpServerBuilder WithGetContextTool(this IMcpServerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSingleton<McpServerTool>(Create);
        return builder;
    }

    public static McpServerTool Create(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return Create(services.GetRequiredService<GetContextMcpTools>(), services);
    }

    public static McpServerTool Create(GetContextMcpTools tools, IServiceProvider? services = null)
    {
        ArgumentNullException.ThrowIfNull(tools);
        var tool = McpServerTool.Create(
            tools.GetContextAsync,
            new McpServerToolCreateOptions
            {
                Name = GetContextToolMetadata.Name,
                Title = GetContextToolMetadata.Title,
                Description = GetContextToolMetadata.Description,
                ReadOnly = true,
                OpenWorld = false,
                UseStructuredContent = true,
                OutputSchema = Cap0001JsonSchemas.Output,
                SerializerOptions = McpJson.Options,
                Services = services
            });
        tool.ProtocolTool.InputSchema = Cap0001JsonSchemas.Input;
        return StrictInputMcpServerTool.Wrap(tool);
    }
}
