using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace RevitMCP.Server;

internal static class GetParameterValuesToolRegistration
{
    public static IMcpServerBuilder WithGetParameterValuesTool(this IMcpServerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSingleton<McpServerTool>(Create);
        return builder;
    }

    public static McpServerTool Create(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return Create(services.GetRequiredService<GetParameterValuesMcpTools>(), services);
    }

    public static McpServerTool Create(GetParameterValuesMcpTools tools, IServiceProvider? services = null)
    {
        ArgumentNullException.ThrowIfNull(tools);
        var tool = McpServerTool.Create(
            tools.GetParameterValuesAsync,
            new McpServerToolCreateOptions
            {
                Name = GetParameterValuesToolMetadata.Name,
                Title = GetParameterValuesToolMetadata.Title,
                Description = GetParameterValuesToolMetadata.Description,
                ReadOnly = true,
                OpenWorld = false,
                UseStructuredContent = true,
                OutputSchema = Cap0005JsonSchemas.Output,
                SerializerOptions = McpJson.Options,
                Services = services
            });
        tool.ProtocolTool.InputSchema = Cap0005JsonSchemas.Input;
        return StrictInputMcpServerTool.Wrap(tool);
    }
}
