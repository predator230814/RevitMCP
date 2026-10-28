using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace BimIt.Revit.Server;

internal static class ReadSpatialRoomsToolRegistration
{
    public static IMcpServerBuilder WithReadSpatialRoomsTool(this IMcpServerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSingleton<McpServerTool>(Create);
        return builder;
    }

    public static McpServerTool Create(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return Create(services.GetRequiredService<ReadSpatialRoomsMcpTools>(), services);
    }

    public static McpServerTool Create(ReadSpatialRoomsMcpTools tools, IServiceProvider? services = null)
    {
        ArgumentNullException.ThrowIfNull(tools);
        var tool = McpServerTool.Create(
            tools.ReadSpatialRoomsAsync,
            new McpServerToolCreateOptions
            {
                Name = ReadSpatialRoomsToolMetadata.Name,
                Title = ReadSpatialRoomsToolMetadata.Title,
                Description = ReadSpatialRoomsToolMetadata.Description,
                ReadOnly = true,
                OpenWorld = false,
                UseStructuredContent = true,
                OutputSchema = ReadSpatialRoomsJsonSchemas.Output,
                SerializerOptions = McpJson.Options,
                Services = services
            });
        tool.ProtocolTool.InputSchema = ReadSpatialRoomsJsonSchemas.Input;
        return StrictInputMcpServerTool.Wrap(tool);
    }
}
