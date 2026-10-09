using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace RevitMCP.Server;

internal static class GetWarningsToolRegistration
{
    public static IMcpServerBuilder WithGetWarningsTool(this IMcpServerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSingleton<McpServerTool>(Create);
        return builder;
    }

    public static McpServerTool Create(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return Create(services.GetRequiredService<GetWarningsMcpTools>(), services);
    }

    public static McpServerTool Create(GetWarningsMcpTools tools, IServiceProvider? services = null)
    {
        ArgumentNullException.ThrowIfNull(tools);
        var tool = McpServerTool.Create(
            tools.GetWarningsAsync,
            new McpServerToolCreateOptions
            {
                Name = GetWarningsToolMetadata.Name,
                Title = GetWarningsToolMetadata.Title,
                Description = GetWarningsToolMetadata.Description,
                ReadOnly = true,
                OpenWorld = false,
                UseStructuredContent = true,
                OutputSchema = GetWarningsJsonSchemas.Output,
                SerializerOptions = McpJson.Options,
                Services = services
            });
        tool.ProtocolTool.InputSchema = GetWarningsJsonSchemas.Input;
        return StrictInputMcpServerTool.Wrap(tool);
    }
}
