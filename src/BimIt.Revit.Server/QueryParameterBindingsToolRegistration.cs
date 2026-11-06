using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace BimIt.Revit.Server;

internal static class QueryParameterBindingsToolRegistration
{
    public static IMcpServerBuilder WithQueryParameterBindingsTool(this IMcpServerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSingleton<McpServerTool>(Create);
        return builder;
    }

    public static McpServerTool Create(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return Create(services.GetRequiredService<QueryParameterBindingsMcpTools>(), services);
    }

    public static McpServerTool Create(QueryParameterBindingsMcpTools tools, IServiceProvider? services = null)
    {
        ArgumentNullException.ThrowIfNull(tools);
        var tool = McpServerTool.Create(
            tools.QueryParameterBindingsAsync,
            new McpServerToolCreateOptions
            {
                Name = QueryParameterBindingsToolMetadata.Name,
                Title = QueryParameterBindingsToolMetadata.Title,
                Description = QueryParameterBindingsToolMetadata.Description,
                ReadOnly = true,
                OpenWorld = false,
                UseStructuredContent = true,
                OutputSchema = QueryParameterBindingsJsonSchemas.Output,
                SerializerOptions = McpJson.Options,
                Services = services
            });
        tool.ProtocolTool.InputSchema = QueryParameterBindingsJsonSchemas.Input;
        return StrictInputMcpServerTool.Wrap(tool);
    }
}
