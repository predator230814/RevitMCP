namespace RevitMCP.Contracts;

public static class BridgeProtocol
{
    public const int CurrentVersion = 1;

    public static IReadOnlyList<int> SupportedVersions { get; } = [CurrentVersion];
}
