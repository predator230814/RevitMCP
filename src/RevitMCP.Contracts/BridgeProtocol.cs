namespace RevitMCP.Contracts;

public static class BridgeProtocol
{
    public const int HandshakeVersion = 1;

    public const int GetContextVersion = 2;

    public const int QueryElementsVersion = 3;

    public const int GetElementsVersion = 4;

    public const int DescribeParametersVersion = 5;

    public const int CurrentVersion = DescribeParametersVersion;

    public static IReadOnlyList<int> HandshakeOnlyVersions { get; } = [HandshakeVersion];

    public static IReadOnlyList<int> GetContextVersions { get; } = [GetContextVersion, HandshakeVersion];

    public static IReadOnlyList<int> QueryElementsVersions { get; } =
        [QueryElementsVersion, GetContextVersion, HandshakeVersion];

    public static IReadOnlyList<int> GetElementsVersions { get; } =
        [GetElementsVersion, QueryElementsVersion, GetContextVersion, HandshakeVersion];

    public static IReadOnlyList<int> SupportedVersions { get; } =
        [DescribeParametersVersion, GetElementsVersion, QueryElementsVersion, GetContextVersion, HandshakeVersion];

    public static bool SupportsGetContext(int version) =>
        version is GetContextVersion or QueryElementsVersion or GetElementsVersion or DescribeParametersVersion;

    public static bool SupportsQueryElements(int version) =>
        version is QueryElementsVersion or GetElementsVersion or DescribeParametersVersion;

    public static bool SupportsGetElements(int version) =>
        version is GetElementsVersion or DescribeParametersVersion;

    public static bool SupportsDescribeParameters(int version) =>
        version is DescribeParametersVersion;
}
