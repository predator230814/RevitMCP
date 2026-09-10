namespace RevitMCP.Server;

public static class Program
{
    public static Task<int> Main(string[] args)
    {
        return ServerHost.RunAsync(args);
    }
}
