namespace RevitMCP.Contracts;

public static class BridgeProtocol
{
    public const int HandshakeVersion = 1;

    public const int GetContextVersion = 2;

    public const int CurrentVersion = GetContextVersion;

    public static IReadOnlyList<int> HandshakeOnlyVersions { get; } = [HandshakeVersion];

    public static IReadOnlyList<int> SupportedVersions { get; } = [GetContextVersion, HandshakeVersion];
}
