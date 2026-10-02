using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace RevitMCP.Server;

internal static class PreviewParameterUpdatesToolRegistration
{
    public static IMcpServerBuilder WithPreviewParameterUpdatesTool(this IMcpServerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSingleton<McpServerTool>(Create);
        return builder;
    }

    public static McpServerTool Create(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return Create(services.GetRequiredService<PreviewParameterUpdatesMcpTools>(), services);
    }

    public static McpServerTool Create(PreviewParameterUpdatesMcpTools tools, IServiceProvider? services = null)
    {
        ArgumentNullException.ThrowIfNull(tools);
        var tool = McpServerTool.Create(
            tools.PreviewParameterUpdatesAsync,
            new McpServerToolCreateOptions
            {
                Name = PreviewParameterUpdatesToolMetadata.Name,
                Title = PreviewParameterUpdatesToolMetadata.Title,
                Description = PreviewParameterUpdatesToolMetadata.Description,
                ReadOnly = false,
                OpenWorld = false,
                UseStructuredContent = true,
                OutputSchema = Cap0007JsonSchemas.Output,
                SerializerOptions = McpJson.Options,
                Services = services
            });
        tool.ProtocolTool.InputSchema = Cap0007JsonSchemas.Input;
        var annotations = tool.ProtocolTool.Annotations ?? new ToolAnnotations();
        annotations.ReadOnlyHint = false;
        annotations.DestructiveHint = false;
        annotations.IdempotentHint = false;
        annotations.OpenWorldHint = false;
        tool.ProtocolTool.Annotations = annotations;
        return StrictInputMcpServerTool.Wrap(tool);
    }
}
