using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace BimIt.Revit.Server;

internal static class ReadOperationToolRegistration
{
    public static IMcpServerBuilder WithReadOperationTool(this IMcpServerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSingleton<McpServerTool>(Create);
        return builder;
    }

    public static McpServerTool Create(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return Create(services.GetRequiredService<ReadOperationMcpTools>(), services);
    }

    public static McpServerTool Create(ReadOperationMcpTools tools, IServiceProvider? services = null)
    {
        ArgumentNullException.ThrowIfNull(tools);
        var tool = McpServerTool.Create(
            tools.ReadAsync,
            new McpServerToolCreateOptions
            {
                Name = ReadOperationToolMetadata.Name,
                Title = ReadOperationToolMetadata.Title,
                Description = ReadOperationToolMetadata.Description,
                ReadOnly = true,
                OpenWorld = false,
                UseStructuredContent = true,
                OutputSchema = ReadOperationJsonSchemas.Output,
                SerializerOptions = McpJson.Options,
                Services = services
            });
        tool.ProtocolTool.InputSchema = ReadOperationJsonSchemas.Input;
        return StrictInputMcpServerTool.Wrap(tool);
    }
}
