namespace RevitMCP.Contracts;

public static class BridgeProtocol
{
    public const int HandshakeVersion = 1;

    public const int GetContextVersion = 2;

    public const int QueryElementsVersion = 3;

    public const int GetElementsVersion = 4;

    public const int DescribeParametersVersion = 5;

    public const int GetParameterValuesVersion = 6;

    public const int CurrentVersion = GetParameterValuesVersion;

    public static IReadOnlyList<int> HandshakeOnlyVersions { get; } = [HandshakeVersion];

    public static IReadOnlyList<int> GetContextVersions { get; } = [GetContextVersion, HandshakeVersion];

    public static IReadOnlyList<int> QueryElementsVersions { get; } =
        [QueryElementsVersion, GetContextVersion, HandshakeVersion];

    public static IReadOnlyList<int> GetElementsVersions { get; } =
        [GetElementsVersion, QueryElementsVersion, GetContextVersion, HandshakeVersion];

    public static IReadOnlyList<int> DescribeParametersVersions { get; } =
        [DescribeParametersVersion, GetElementsVersion, QueryElementsVersion, GetContextVersion, HandshakeVersion];

    public static IReadOnlyList<int> SupportedVersions { get; } =
        [
            GetParameterValuesVersion,
            DescribeParametersVersion,
            GetElementsVersion,
            QueryElementsVersion,
            GetContextVersion,
            HandshakeVersion
        ];

    public static bool SupportsGetContext(int version) =>
        version is GetContextVersion
            or QueryElementsVersion
            or GetElementsVersion
            or DescribeParametersVersion
            or GetParameterValuesVersion;

    public static bool SupportsQueryElements(int version) =>
        version is QueryElementsVersion
            or GetElementsVersion
            or DescribeParametersVersion
            or GetParameterValuesVersion;

    public static bool SupportsGetElements(int version) =>
        version is GetElementsVersion or DescribeParametersVersion or GetParameterValuesVersion;

    public static bool SupportsDescribeParameters(int version) =>
        version is DescribeParametersVersion or GetParameterValuesVersion;

    public static bool SupportsGetParameterValues(int version) =>
        version is GetParameterValuesVersion;
}
