using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace RevitMCP.Server;

internal static class DescribeParametersToolRegistration
{
    public static IMcpServerBuilder WithDescribeParametersTool(this IMcpServerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSingleton<McpServerTool>(Create);
        return builder;
    }

    public static McpServerTool Create(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return Create(services.GetRequiredService<DescribeParametersMcpTools>(), services);
    }

    public static McpServerTool Create(DescribeParametersMcpTools tools, IServiceProvider? services = null)
    {
        ArgumentNullException.ThrowIfNull(tools);
        var tool = McpServerTool.Create(
            tools.DescribeParametersAsync,
            new McpServerToolCreateOptions
            {
                Name = DescribeParametersToolMetadata.Name,
                Title = DescribeParametersToolMetadata.Title,
                Description = DescribeParametersToolMetadata.Description,
                ReadOnly = true,
                OpenWorld = false,
                UseStructuredContent = true,
                OutputSchema = Cap0004JsonSchemas.Output,
                SerializerOptions = McpJson.Options,
                Services = services
            });
        tool.ProtocolTool.InputSchema = Cap0004JsonSchemas.Input;
        return StrictInputMcpServerTool.Wrap(tool);
    }
}
