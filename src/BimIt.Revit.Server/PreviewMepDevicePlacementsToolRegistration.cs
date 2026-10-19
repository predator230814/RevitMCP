using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace BimIt.Revit.Server;

internal static class PreviewMepDevicePlacementsToolRegistration
{
    public static IMcpServerBuilder WithPreviewMepDevicePlacementsTool(this IMcpServerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSingleton<McpServerTool>(Create);
        return builder;
    }

    public static McpServerTool Create(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return Create(services.GetRequiredService<PreviewMepDevicePlacementsMcpTools>(), services);
    }

    public static McpServerTool Create(PreviewMepDevicePlacementsMcpTools tools, IServiceProvider? services = null)
    {
        ArgumentNullException.ThrowIfNull(tools);
        var tool = McpServerTool.Create(
            tools.PreviewMepDevicePlacementsAsync,
            new McpServerToolCreateOptions
            {
                Name = PreviewMepDevicePlacementsToolMetadata.Name,
                Title = PreviewMepDevicePlacementsToolMetadata.Title,
                Description = PreviewMepDevicePlacementsToolMetadata.Description,
                ReadOnly = false,
                OpenWorld = false,
                UseStructuredContent = true,
                OutputSchema = PreviewMepDevicePlacementsJsonSchemas.Output,
                SerializerOptions = McpJson.Options,
                Services = services
            });
        tool.ProtocolTool.InputSchema = PreviewMepDevicePlacementsJsonSchemas.Input;
        var annotations = tool.ProtocolTool.Annotations ?? new ToolAnnotations();
        annotations.ReadOnlyHint = false;
        annotations.DestructiveHint = false;
        annotations.IdempotentHint = false;
        annotations.OpenWorldHint = false;
        tool.ProtocolTool.Annotations = annotations;
        return StrictInputMcpServerTool.Wrap(tool);
    }
}
