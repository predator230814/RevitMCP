namespace RevitMCP.Contracts;

public static class BridgeProtocol
{
    public const int HandshakeVersion = 1;

    public const int GetContextVersion = 2;

    public const int QueryElementsVersion = 3;

    public const int GetElementsVersion = 4;

    public const int DescribeParametersVersion = 5;

    public const int GetParameterValuesVersion = 6;

    public const int GetMepTopologyVersion = 7;

    public const int PreviewParameterUpdatesVersion = 8;

    public const int RequestParameterUpdateReviewVersion = 9;

    public const int ApplyParameterUpdatesVersion = 10;

    public const int GetWarningsVersion = 11;

    public const int CurrentVersion = GetWarningsVersion;

    public static IReadOnlyList<int> HandshakeOnlyVersions { get; } = [HandshakeVersion];

    public static IReadOnlyList<int> GetContextVersions { get; } = [GetContextVersion, HandshakeVersion];

    public static IReadOnlyList<int> QueryElementsVersions { get; } =
        [QueryElementsVersion, GetContextVersion, HandshakeVersion];

    public static IReadOnlyList<int> GetElementsVersions { get; } =
        [GetElementsVersion, QueryElementsVersion, GetContextVersion, HandshakeVersion];

    public static IReadOnlyList<int> DescribeParametersVersions { get; } =
        [DescribeParametersVersion, GetElementsVersion, QueryElementsVersion, GetContextVersion, HandshakeVersion];

    public static IReadOnlyList<int> GetParameterValuesVersions { get; } =
        [
            GetParameterValuesVersion,
            DescribeParametersVersion,
            GetElementsVersion,
            QueryElementsVersion,
            GetContextVersion,
            HandshakeVersion
        ];

    public static IReadOnlyList<int> GetMepTopologyVersions { get; } =
        [
            GetMepTopologyVersion,
            GetParameterValuesVersion,
            DescribeParametersVersion,
            GetElementsVersion,
            QueryElementsVersion,
            GetContextVersion,
            HandshakeVersion
        ];

    public static IReadOnlyList<int> PreviewParameterUpdatesVersions { get; } =
        [
            PreviewParameterUpdatesVersion,
            GetMepTopologyVersion,
            GetParameterValuesVersion,
            DescribeParametersVersion,
            GetElementsVersion,
            QueryElementsVersion,
            GetContextVersion,
            HandshakeVersion
        ];

    public static IReadOnlyList<int> RequestParameterUpdateReviewVersions { get; } =
        [
            RequestParameterUpdateReviewVersion,
            PreviewParameterUpdatesVersion,
            GetMepTopologyVersion,
            GetParameterValuesVersion,
            DescribeParametersVersion,
            GetElementsVersion,
            QueryElementsVersion,
            GetContextVersion,
            HandshakeVersion
        ];

    public static IReadOnlyList<int> ApplyParameterUpdatesVersions { get; } =
        [
            ApplyParameterUpdatesVersion,
            RequestParameterUpdateReviewVersion,
            PreviewParameterUpdatesVersion,
            GetMepTopologyVersion,
            GetParameterValuesVersion,
            DescribeParametersVersion,
            GetElementsVersion,
            QueryElementsVersion,
            GetContextVersion,
            HandshakeVersion
        ];

    public static IReadOnlyList<int> SupportedVersions { get; } =
        [
            GetWarningsVersion,
            ApplyParameterUpdatesVersion,
            RequestParameterUpdateReviewVersion,
            PreviewParameterUpdatesVersion,
            GetMepTopologyVersion,
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
            or GetParameterValuesVersion
            or GetMepTopologyVersion
            or PreviewParameterUpdatesVersion
            or RequestParameterUpdateReviewVersion
            or ApplyParameterUpdatesVersion
            or GetWarningsVersion;

    public static bool SupportsQueryElements(int version) =>
        version is QueryElementsVersion
            or GetElementsVersion
            or DescribeParametersVersion
            or GetParameterValuesVersion
            or GetMepTopologyVersion
            or PreviewParameterUpdatesVersion
            or RequestParameterUpdateReviewVersion
            or ApplyParameterUpdatesVersion
            or GetWarningsVersion;

    public static bool SupportsGetElements(int version) =>
        version is GetElementsVersion
            or DescribeParametersVersion
            or GetParameterValuesVersion
            or GetMepTopologyVersion
            or PreviewParameterUpdatesVersion
            or RequestParameterUpdateReviewVersion
            or ApplyParameterUpdatesVersion
            or GetWarningsVersion;

    public static bool SupportsDescribeParameters(int version) =>
        version is DescribeParametersVersion
            or GetParameterValuesVersion
            or GetMepTopologyVersion
            or PreviewParameterUpdatesVersion
            or RequestParameterUpdateReviewVersion
            or ApplyParameterUpdatesVersion
            or GetWarningsVersion;

    public static bool SupportsGetParameterValues(int version) =>
        version is GetParameterValuesVersion
            or GetMepTopologyVersion
            or PreviewParameterUpdatesVersion
            or RequestParameterUpdateReviewVersion
            or ApplyParameterUpdatesVersion
            or GetWarningsVersion;

    public static bool SupportsGetMepTopology(int version) =>
        version is GetMepTopologyVersion
            or PreviewParameterUpdatesVersion
            or RequestParameterUpdateReviewVersion
            or ApplyParameterUpdatesVersion
            or GetWarningsVersion;

    public static bool SupportsPreviewParameterUpdates(int version) =>
        version is PreviewParameterUpdatesVersion
            or RequestParameterUpdateReviewVersion
            or ApplyParameterUpdatesVersion
            or GetWarningsVersion;

    public static bool SupportsRequestParameterUpdateReview(int version) =>
        version is RequestParameterUpdateReviewVersion
            or ApplyParameterUpdatesVersion
            or GetWarningsVersion;

    public static bool SupportsApplyParameterUpdates(int version) =>
        version is ApplyParameterUpdatesVersion or GetWarningsVersion;

    public static bool SupportsGetWarnings(int version) =>
        version is GetWarningsVersion;
}
