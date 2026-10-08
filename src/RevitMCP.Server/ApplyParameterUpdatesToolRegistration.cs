using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace RevitMCP.Server;

internal static class ApplyParameterUpdatesToolRegistration
{
    public static IMcpServerBuilder WithApplyParameterUpdatesTool(this IMcpServerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSingleton<McpServerTool>(Create);
        return builder;
    }

    public static McpServerTool Create(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return Create(services.GetRequiredService<ApplyParameterUpdatesMcpTools>(), services);
    }

    public static McpServerTool Create(ApplyParameterUpdatesMcpTools tools, IServiceProvider? services = null)
    {
        ArgumentNullException.ThrowIfNull(tools);
        var tool = McpServerTool.Create(
            tools.ApplyParameterUpdatesAsync,
            new McpServerToolCreateOptions
            {
                Name = ApplyParameterUpdatesToolMetadata.Name,
                Title = ApplyParameterUpdatesToolMetadata.Title,
                Description = ApplyParameterUpdatesToolMetadata.Description,
                ReadOnly = false,
                Destructive = true,
                Idempotent = false,
                OpenWorld = false,
                UseStructuredContent = true,
                OutputSchema = ApplyParameterUpdatesJsonSchemas.Output,
                SerializerOptions = McpJson.Options,
                Services = services
            });
        tool.ProtocolTool.InputSchema = ApplyParameterUpdatesJsonSchemas.Input;
        var annotations = tool.ProtocolTool.Annotations ?? new ToolAnnotations();
        annotations.ReadOnlyHint = false;
        annotations.DestructiveHint = true;
        annotations.IdempotentHint = false;
        annotations.OpenWorldHint = false;
        tool.ProtocolTool.Annotations = annotations;
        return StrictInputMcpServerTool.Wrap(tool);
    }
}
