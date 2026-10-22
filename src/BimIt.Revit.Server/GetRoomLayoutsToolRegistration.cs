using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace BimIt.Revit.Server;

internal static class GetRoomLayoutsToolRegistration
{
    public static IMcpServerBuilder WithGetRoomLayoutsTool(this IMcpServerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSingleton<McpServerTool>(Create);
        return builder;
    }

    public static McpServerTool Create(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return Create(services.GetRequiredService<GetRoomLayoutsMcpTools>(), services);
    }

    public static McpServerTool Create(GetRoomLayoutsMcpTools tools, IServiceProvider? services = null)
    {
        ArgumentNullException.ThrowIfNull(tools);
        var tool = McpServerTool.Create(
            tools.GetRoomLayoutsAsync,
            new McpServerToolCreateOptions
            {
                Name = GetRoomLayoutsToolMetadata.Name,
                Title = GetRoomLayoutsToolMetadata.Title,
                Description = GetRoomLayoutsToolMetadata.Description,
                ReadOnly = true,
                OpenWorld = false,
                UseStructuredContent = true,
                OutputSchema = GetRoomLayoutsJsonSchemas.Output,
                SerializerOptions = McpJson.Options,
                Services = services
            });
        tool.ProtocolTool.InputSchema = GetRoomLayoutsJsonSchemas.Input;
        return StrictInputMcpServerTool.Wrap(tool);
    }
}
