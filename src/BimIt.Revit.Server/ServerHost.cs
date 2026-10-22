using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using BimIt.Revit.Bridge;

namespace BimIt.Revit.Server;

internal static class ServerHost
{
    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        var builder = CreateBuilder(args);
        var host = builder.Build();
        await host.RunAsync(cancellationToken).ConfigureAwait(false);
        return 0;
    }

    public static HostApplicationBuilder CreateBuilder(
        string[]? args = null,
        Action<IServiceCollection>? configure = null)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = args
        });
        builder.Logging.ClearProviders();
        AddServices(builder.Services);
        configure?.Invoke(builder.Services);
        return builder;
    }

    internal static void AddServices(IServiceCollection services)
    {
        services.AddSingleton<ServerTimeouts>();
        services.AddSingleton<IWindowsSession, ProcessWindowsSession>();
        services.AddSingleton<IRegistrationStore, FileRegistrationStore>();
        services.AddSingleton<IProcessInspector, WindowsProcessInspector>();
        services.AddSingleton<IBridgeClientFactory, NamedPipeBridgeClientFactory>();
        services.AddSingleton<IRevitInstanceDiscovery, LocalRevitInstanceDiscovery>();
        services.AddSingleton<GetContextApplicationService>();
        services.AddSingleton<GetContextMcpTools>();
        services.AddSingleton<QueryElementsApplicationService>();
        services.AddSingleton<QueryElementsMcpTools>();
        services.AddSingleton<GetElementsApplicationService>();
        services.AddSingleton<GetElementsMcpTools>();
        services.AddSingleton<DescribeParametersApplicationService>();
        services.AddSingleton<DescribeParametersMcpTools>();
        services.AddSingleton<GetParameterValuesApplicationService>();
        services.AddSingleton<GetParameterValuesMcpTools>();
        services.AddSingleton<GetMepTopologyApplicationService>();
        services.AddSingleton<GetMepTopologyMcpTools>();
        services.AddSingleton<PreviewParameterUpdatesApplicationService>();
        services.AddSingleton<PreviewParameterUpdatesMcpTools>();
        services.AddSingleton<RequestParameterUpdateReviewApplicationService>();
        services.AddSingleton<RequestParameterUpdateReviewMcpTools>();
        services.AddSingleton<ApplyParameterUpdatesApplicationService>();
        services.AddSingleton<ApplyParameterUpdatesMcpTools>();
        services.AddSingleton<GetWarningsApplicationService>();
        services.AddSingleton<GetWarningsMcpTools>();
        services.AddSingleton<GetSelectionApplicationService>();
        services.AddSingleton<GetSelectionMcpTools>();
        services.AddSingleton<PreviewMepDevicePlacementsApplicationService>();
        services.AddSingleton<PreviewMepDevicePlacementsMcpTools>();
        services.AddSingleton<RequestMepDevicePlacementReviewApplicationService>();
        services.AddSingleton<RequestMepDevicePlacementReviewMcpTools>();
        services.AddSingleton<ApplyMepDevicePlacementsApplicationService>();
        services.AddSingleton<ApplyMepDevicePlacementsMcpTools>();
        services.AddSingleton<GetRoomLayoutsApplicationService>();
        services.AddSingleton<GetRoomLayoutsMcpTools>();
        services.AddMcpServer(options =>
            {
                options.ServerInfo = new Implementation
                {
                    Name = "RevitMCP.Server",
                    Version = "0.1.0"
                };
            })
            .WithStdioServerTransport()
            .WithGetContextTool()
            .WithQueryElementsTool()
            .WithGetElementsTool()
            .WithDescribeParametersTool()
            .WithGetParameterValuesTool()
            .WithGetMepTopologyTool()
            .WithPreviewParameterUpdatesTool()
            .WithRequestParameterUpdateReviewTool()
            .WithApplyParameterUpdatesTool()
            .WithGetWarningsTool()
            .WithGetSelectionTool()
            .WithPreviewMepDevicePlacementsTool()
            .WithRequestMepDevicePlacementReviewTool()
            .WithApplyMepDevicePlacementsTool()
            .WithGetRoomLayoutsTool();
    }
}
