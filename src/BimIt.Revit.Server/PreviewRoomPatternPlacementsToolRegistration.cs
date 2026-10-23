using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace BimIt.Revit.Server;

internal static class PreviewRoomPatternPlacementsToolRegistration
{
    public static IMcpServerBuilder WithPreviewRoomPatternPlacementsTool(this IMcpServerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSingleton<McpServerTool>(Create);
        return builder;
    }

    public static McpServerTool Create(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return Create(services.GetRequiredService<PreviewRoomPatternPlacementsMcpTools>(), services);
    }

    public static McpServerTool Create(PreviewRoomPatternPlacementsMcpTools tools, IServiceProvider? services = null)
    {
        ArgumentNullException.ThrowIfNull(tools);
        var tool = McpServerTool.Create(
            tools.PreviewRoomPatternPlacementsAsync,
            new McpServerToolCreateOptions
            {
                Name = PreviewRoomPatternPlacementsToolMetadata.Name,
                Title = PreviewRoomPatternPlacementsToolMetadata.Title,
                Description = PreviewRoomPatternPlacementsToolMetadata.Description,
                ReadOnly = false,
                OpenWorld = false,
                UseStructuredContent = true,
                OutputSchema = PreviewRoomPatternPlacementsJsonSchemas.Output,
                SerializerOptions = McpJson.Options,
                Services = services
            });
        tool.ProtocolTool.InputSchema = PreviewRoomPatternPlacementsJsonSchemas.Input;
        return StrictInputMcpServerTool.Wrap(tool);
    }
}
