using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace RevitMCP.Server;

internal static class GetMepTopologyToolRegistration
{
    public static IMcpServerBuilder WithGetMepTopologyTool(this IMcpServerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSingleton<McpServerTool>(Create);
        return builder;
    }

    public static McpServerTool Create(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return Create(services.GetRequiredService<GetMepTopologyMcpTools>(), services);
    }

    public static McpServerTool Create(GetMepTopologyMcpTools tools, IServiceProvider? services = null)
    {
        ArgumentNullException.ThrowIfNull(tools);
        var tool = McpServerTool.Create(
            tools.GetMepTopologyAsync,
            new McpServerToolCreateOptions
            {
                Name = GetMepTopologyToolMetadata.Name,
                Title = GetMepTopologyToolMetadata.Title,
                Description = GetMepTopologyToolMetadata.Description,
                ReadOnly = true,
                OpenWorld = false,
                UseStructuredContent = true,
                OutputSchema = Cap0006JsonSchemas.Output,
                SerializerOptions = McpJson.Options,
                Services = services
            });
        tool.ProtocolTool.InputSchema = Cap0006JsonSchemas.Input;
        return StrictInputMcpServerTool.Wrap(tool);
    }
}
