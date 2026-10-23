using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace BimIt.Revit.Server;

internal static class GetMepDeviceTypesToolRegistration
{
    public static IMcpServerBuilder WithGetMepDeviceTypesTool(this IMcpServerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSingleton<McpServerTool>(Create);
        return builder;
    }

    public static McpServerTool Create(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return Create(services.GetRequiredService<GetMepDeviceTypesMcpTools>(), services);
    }

    public static McpServerTool Create(GetMepDeviceTypesMcpTools tools, IServiceProvider? services = null)
    {
        ArgumentNullException.ThrowIfNull(tools);
        var tool = McpServerTool.Create(
            tools.GetMepDeviceTypesAsync,
            new McpServerToolCreateOptions
            {
                Name = GetMepDeviceTypesToolMetadata.Name,
                Title = GetMepDeviceTypesToolMetadata.Title,
                Description = GetMepDeviceTypesToolMetadata.Description,
                ReadOnly = true,
                OpenWorld = false,
                UseStructuredContent = true,
                OutputSchema = GetMepDeviceTypesJsonSchemas.Output,
                SerializerOptions = McpJson.Options,
                Services = services
            });
        tool.ProtocolTool.InputSchema = GetMepDeviceTypesJsonSchemas.Input;
        return StrictInputMcpServerTool.Wrap(tool);
    }
}
