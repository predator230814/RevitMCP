using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace RevitMCP.Server;

internal static class RequestParameterUpdateReviewToolRegistration
{
    public static IMcpServerBuilder WithRequestParameterUpdateReviewTool(this IMcpServerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSingleton<McpServerTool>(Create);
        return builder;
    }

    public static McpServerTool Create(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return Create(services.GetRequiredService<RequestParameterUpdateReviewMcpTools>(), services);
    }

    public static McpServerTool Create(RequestParameterUpdateReviewMcpTools tools, IServiceProvider? services = null)
    {
        ArgumentNullException.ThrowIfNull(tools);
        var tool = McpServerTool.Create(
            tools.RequestParameterUpdateReviewAsync,
            new McpServerToolCreateOptions
            {
                Name = RequestParameterUpdateReviewToolMetadata.Name,
                Title = RequestParameterUpdateReviewToolMetadata.Title,
                Description = RequestParameterUpdateReviewToolMetadata.Description,
                ReadOnly = false,
                OpenWorld = false,
                UseStructuredContent = true,
                OutputSchema = RequestParameterUpdateReviewJsonSchemas.Output,
                SerializerOptions = McpJson.Options,
                Services = services
            });
        tool.ProtocolTool.InputSchema = RequestParameterUpdateReviewJsonSchemas.Input;
        var annotations = tool.ProtocolTool.Annotations ?? new ToolAnnotations();
        annotations.ReadOnlyHint = false;
        annotations.DestructiveHint = false;
        annotations.IdempotentHint = false;
        annotations.OpenWorldHint = false;
        tool.ProtocolTool.Annotations = annotations;
        return StrictInputMcpServerTool.Wrap(tool);
    }
}
