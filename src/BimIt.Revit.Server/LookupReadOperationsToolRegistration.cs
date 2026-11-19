using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace BimIt.Revit.Server;

internal static class LookupReadOperationsToolRegistration
{
    public static IMcpServerBuilder WithLookupReadOperationsTool(this IMcpServerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSingleton<McpServerTool>(Create);
        return builder;
    }

    public static McpServerTool Create(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return Create(services.GetRequiredService<LookupReadOperationsMcpTools>(), services);
    }

    public static McpServerTool Create(LookupReadOperationsMcpTools tools, IServiceProvider? services = null)
    {
        ArgumentNullException.ThrowIfNull(tools);
        var tool = McpServerTool.Create(
            tools.LookupAsync,
            new McpServerToolCreateOptions
            {
                Name = LookupReadOperationsToolMetadata.Name,
                Title = LookupReadOperationsToolMetadata.Title,
                Description = LookupReadOperationsToolMetadata.Description,
                ReadOnly = true,
                OpenWorld = false,
                UseStructuredContent = true,
                OutputSchema = LookupReadOperationsJsonSchemas.Output,
                SerializerOptions = McpJson.Options,
                Services = services
            });
        tool.ProtocolTool.InputSchema = LookupReadOperationsJsonSchemas.Input;
        return StrictInputMcpServerTool.Wrap(tool);
    }
}
