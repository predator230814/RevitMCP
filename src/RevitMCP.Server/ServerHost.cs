using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using RevitMCP.Bridge;

namespace RevitMCP.Server;

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
        services.AddMcpServer(options =>
            {
                options.ServerInfo = new Implementation
                {
                    Name = "RevitMCP.Server",
                    Version = "0.1.0"
                };
            })
            .WithStdioServerTransport()
            .WithGetContextTool();
    }
}
