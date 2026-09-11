using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace RevitMCP.Server;

internal static class QueryElementsToolRegistration
{
    public static IMcpServerBuilder WithQueryElementsTool(this IMcpServerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSingleton<McpServerTool>(Create);
        return builder;
    }

    public static McpServerTool Create(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return Create(services.GetRequiredService<QueryElementsMcpTools>(), services);
    }

    public static McpServerTool Create(QueryElementsMcpTools tools, IServiceProvider? services = null)
    {
        ArgumentNullException.ThrowIfNull(tools);
        var tool = McpServerTool.Create(
            tools.QueryElementsAsync,
            new McpServerToolCreateOptions
            {
                Name = QueryElementsToolMetadata.Name,
                Title = QueryElementsToolMetadata.Title,
                Description = QueryElementsToolMetadata.Description,
                ReadOnly = true,
                OpenWorld = false,
                UseStructuredContent = true,
                OutputSchema = Cap0002JsonSchemas.Output,
                SerializerOptions = McpJson.Options,
                Services = services
            });
        tool.ProtocolTool.InputSchema = Cap0002JsonSchemas.Input;
        return StrictInputMcpServerTool.Wrap(tool);
    }
}
