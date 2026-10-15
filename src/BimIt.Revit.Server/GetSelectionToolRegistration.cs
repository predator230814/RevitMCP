using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace BimIt.Revit.Server;

internal static class GetSelectionToolRegistration
{
    public static IMcpServerBuilder WithGetSelectionTool(this IMcpServerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSingleton<McpServerTool>(Create);
        return builder;
    }

    public static McpServerTool Create(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return Create(services.GetRequiredService<GetSelectionMcpTools>(), services);
    }

    public static McpServerTool Create(GetSelectionMcpTools tools, IServiceProvider? services = null)
    {
        ArgumentNullException.ThrowIfNull(tools);
        var tool = McpServerTool.Create(
            tools.GetSelectionAsync,
            new McpServerToolCreateOptions
            {
                Name = GetSelectionToolMetadata.Name,
                Title = GetSelectionToolMetadata.Title,
                Description = GetSelectionToolMetadata.Description,
                ReadOnly = true,
                OpenWorld = false,
                UseStructuredContent = true,
                OutputSchema = GetSelectionJsonSchemas.Output,
                SerializerOptions = McpJson.Options,
                Services = services
            });
        tool.ProtocolTool.InputSchema = GetSelectionJsonSchemas.Input;
        return StrictInputMcpServerTool.Wrap(tool);
    }
}
