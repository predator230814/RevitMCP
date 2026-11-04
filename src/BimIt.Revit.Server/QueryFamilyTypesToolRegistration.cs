using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace BimIt.Revit.Server;

internal static class QueryFamilyTypesToolRegistration
{
    public static IMcpServerBuilder WithQueryFamilyTypesTool(this IMcpServerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSingleton<McpServerTool>(Create);
        return builder;
    }

    public static McpServerTool Create(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return Create(services.GetRequiredService<QueryFamilyTypesMcpTools>(), services);
    }

    public static McpServerTool Create(QueryFamilyTypesMcpTools tools, IServiceProvider? services = null)
    {
        ArgumentNullException.ThrowIfNull(tools);
        var tool = McpServerTool.Create(
            tools.QueryFamilyTypesAsync,
            new McpServerToolCreateOptions
            {
                Name = QueryFamilyTypesToolMetadata.Name,
                Title = QueryFamilyTypesToolMetadata.Title,
                Description = QueryFamilyTypesToolMetadata.Description,
                ReadOnly = true,
                OpenWorld = false,
                UseStructuredContent = true,
                OutputSchema = QueryFamilyTypesJsonSchemas.Output,
                SerializerOptions = McpJson.Options,
                Services = services
            });
        tool.ProtocolTool.InputSchema = QueryFamilyTypesJsonSchemas.Input;
        return StrictInputMcpServerTool.Wrap(tool);
    }
}
