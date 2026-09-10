namespace RevitMCP.Server;

internal sealed class ServerTimeouts
{
    public TimeSpan BootstrapTimeout { get; init; } = TimeSpan.FromSeconds(2);

    public TimeSpan CapabilityTimeout { get; init; } = TimeSpan.FromSeconds(15);
}
