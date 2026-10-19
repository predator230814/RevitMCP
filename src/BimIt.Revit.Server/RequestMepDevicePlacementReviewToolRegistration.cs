using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace BimIt.Revit.Server;

internal static class RequestMepDevicePlacementReviewToolRegistration
{
    public static IMcpServerBuilder WithRequestMepDevicePlacementReviewTool(this IMcpServerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSingleton<McpServerTool>(Create);
        return builder;
    }

    public static McpServerTool Create(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return Create(services.GetRequiredService<RequestMepDevicePlacementReviewMcpTools>(), services);
    }

    public static McpServerTool Create(RequestMepDevicePlacementReviewMcpTools tools, IServiceProvider? services = null)
    {
        ArgumentNullException.ThrowIfNull(tools);
        var tool = McpServerTool.Create(
            tools.RequestMepDevicePlacementReviewAsync,
            new McpServerToolCreateOptions
            {
                Name = RequestMepDevicePlacementReviewToolMetadata.Name,
                Title = RequestMepDevicePlacementReviewToolMetadata.Title,
                Description = RequestMepDevicePlacementReviewToolMetadata.Description,
                ReadOnly = false,
                OpenWorld = false,
                UseStructuredContent = true,
                OutputSchema = RequestMepDevicePlacementReviewJsonSchemas.Output,
                SerializerOptions = McpJson.Options,
                Services = services
            });
        tool.ProtocolTool.InputSchema = RequestMepDevicePlacementReviewJsonSchemas.Input;
        var annotations = tool.ProtocolTool.Annotations ?? new ToolAnnotations();
        annotations.ReadOnlyHint = false;
        annotations.DestructiveHint = false;
        annotations.IdempotentHint = false;
        annotations.OpenWorldHint = false;
        tool.ProtocolTool.Annotations = annotations;
        return StrictInputMcpServerTool.Wrap(tool);
    }
}
