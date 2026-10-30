namespace BimIt.Revit.Contracts;

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

    public const int ContextDocumentIdentityVersion = 12;

    public const int GetSelectionVersion = 13;

    public const int PreviewMepDevicePlacementsVersion = 14;

    public const int RequestMepDevicePlacementReviewVersion = 15;

    public const int ApplyMepDevicePlacementsVersion = 16;

    public const int GetRoomLayoutsVersion = 17;

    public const int GetMepDeviceTypesVersion = 18;

    public const int PreviewRoomPatternPlacementsVersion = 19;

    public const int ReadSpatialRoomsVersion = 20;

    public const int LinkedRoomPatternPreviewVersion = 21;

    public const int CurrentVersion = LinkedRoomPatternPreviewVersion;

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

    public static IReadOnlyList<int> ContextDocumentIdentityVersions { get; } =
        [
            ContextDocumentIdentityVersion,
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

    public static IReadOnlyList<int> GetSelectionVersions { get; } =
        [
            GetSelectionVersion,
            ContextDocumentIdentityVersion,
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

    public static IReadOnlyList<int> PreviewMepDevicePlacementsVersions { get; } =
        [
            PreviewMepDevicePlacementsVersion,
            GetSelectionVersion,
            ContextDocumentIdentityVersion,
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

    public static IReadOnlyList<int> RequestMepDevicePlacementReviewVersions { get; } =
        [
            RequestMepDevicePlacementReviewVersion,
            PreviewMepDevicePlacementsVersion,
            GetSelectionVersion,
            ContextDocumentIdentityVersion,
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

    public static IReadOnlyList<int> ApplyMepDevicePlacementsVersions { get; } =
        [
            ApplyMepDevicePlacementsVersion,
            RequestMepDevicePlacementReviewVersion,
            PreviewMepDevicePlacementsVersion,
            GetSelectionVersion,
            ContextDocumentIdentityVersion,
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

    public static IReadOnlyList<int> GetRoomLayoutsVersions { get; } =
        [
            GetRoomLayoutsVersion,
            ApplyMepDevicePlacementsVersion,
            RequestMepDevicePlacementReviewVersion,
            PreviewMepDevicePlacementsVersion,
            GetSelectionVersion,
            ContextDocumentIdentityVersion,
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

    public static IReadOnlyList<int> GetMepDeviceTypesVersions { get; } =
        [GetMepDeviceTypesVersion, .. GetRoomLayoutsVersions];

    public static IReadOnlyList<int> PreviewRoomPatternPlacementsVersions { get; } =
        [PreviewRoomPatternPlacementsVersion, .. GetMepDeviceTypesVersions];

    public static IReadOnlyList<int> ReadSpatialRoomsVersions { get; } =
        [ReadSpatialRoomsVersion, .. PreviewRoomPatternPlacementsVersions];

    public static IReadOnlyList<int> LinkedRoomPatternPreviewVersions { get; } =
        [LinkedRoomPatternPreviewVersion, .. ReadSpatialRoomsVersions];

    public static IReadOnlyList<int> SupportedVersions => LinkedRoomPatternPreviewVersions;

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
            or GetWarningsVersion
            or ContextDocumentIdentityVersion
            or GetSelectionVersion
            or PreviewMepDevicePlacementsVersion
            or RequestMepDevicePlacementReviewVersion
            or ApplyMepDevicePlacementsVersion
            or GetRoomLayoutsVersion
            or GetMepDeviceTypesVersion
            or PreviewRoomPatternPlacementsVersion
            or ReadSpatialRoomsVersion
            or LinkedRoomPatternPreviewVersion;

    public static bool SupportsQueryElements(int version) =>
        version is QueryElementsVersion
            or GetElementsVersion
            or DescribeParametersVersion
            or GetParameterValuesVersion
            or GetMepTopologyVersion
            or PreviewParameterUpdatesVersion
            or RequestParameterUpdateReviewVersion
            or ApplyParameterUpdatesVersion
            or GetWarningsVersion
            or ContextDocumentIdentityVersion
            or GetSelectionVersion
            or PreviewMepDevicePlacementsVersion
            or RequestMepDevicePlacementReviewVersion
            or ApplyMepDevicePlacementsVersion
            or GetRoomLayoutsVersion
            or GetMepDeviceTypesVersion
            or PreviewRoomPatternPlacementsVersion
            or ReadSpatialRoomsVersion
            or LinkedRoomPatternPreviewVersion;

    public static bool SupportsGetElements(int version) =>
        version is GetElementsVersion
            or DescribeParametersVersion
            or GetParameterValuesVersion
            or GetMepTopologyVersion
            or PreviewParameterUpdatesVersion
            or RequestParameterUpdateReviewVersion
            or ApplyParameterUpdatesVersion
            or GetWarningsVersion
            or ContextDocumentIdentityVersion
            or GetSelectionVersion
            or PreviewMepDevicePlacementsVersion
            or RequestMepDevicePlacementReviewVersion
            or ApplyMepDevicePlacementsVersion
            or GetRoomLayoutsVersion
            or GetMepDeviceTypesVersion
            or PreviewRoomPatternPlacementsVersion
            or ReadSpatialRoomsVersion
            or LinkedRoomPatternPreviewVersion;

    public static bool SupportsDescribeParameters(int version) =>
        version is DescribeParametersVersion
            or GetParameterValuesVersion
            or GetMepTopologyVersion
            or PreviewParameterUpdatesVersion
            or RequestParameterUpdateReviewVersion
            or ApplyParameterUpdatesVersion
            or GetWarningsVersion
            or ContextDocumentIdentityVersion
            or GetSelectionVersion
            or PreviewMepDevicePlacementsVersion
            or RequestMepDevicePlacementReviewVersion
            or ApplyMepDevicePlacementsVersion
            or GetRoomLayoutsVersion
            or GetMepDeviceTypesVersion
            or PreviewRoomPatternPlacementsVersion
            or ReadSpatialRoomsVersion
            or LinkedRoomPatternPreviewVersion;

    public static bool SupportsGetParameterValues(int version) =>
        version is GetParameterValuesVersion
            or GetMepTopologyVersion
            or PreviewParameterUpdatesVersion
            or RequestParameterUpdateReviewVersion
            or ApplyParameterUpdatesVersion
            or GetWarningsVersion
            or ContextDocumentIdentityVersion
            or GetSelectionVersion
            or PreviewMepDevicePlacementsVersion
            or RequestMepDevicePlacementReviewVersion
            or ApplyMepDevicePlacementsVersion
            or GetRoomLayoutsVersion
            or GetMepDeviceTypesVersion
            or PreviewRoomPatternPlacementsVersion
            or ReadSpatialRoomsVersion
            or LinkedRoomPatternPreviewVersion;

    public static bool SupportsGetMepTopology(int version) =>
        version is GetMepTopologyVersion
            or PreviewParameterUpdatesVersion
            or RequestParameterUpdateReviewVersion
            or ApplyParameterUpdatesVersion
            or GetWarningsVersion
            or ContextDocumentIdentityVersion
            or GetSelectionVersion
            or PreviewMepDevicePlacementsVersion
            or RequestMepDevicePlacementReviewVersion
            or ApplyMepDevicePlacementsVersion
            or GetRoomLayoutsVersion
            or GetMepDeviceTypesVersion
            or PreviewRoomPatternPlacementsVersion
            or ReadSpatialRoomsVersion
            or LinkedRoomPatternPreviewVersion;

    public static bool SupportsPreviewParameterUpdates(int version) =>
        version is PreviewParameterUpdatesVersion
            or RequestParameterUpdateReviewVersion
            or ApplyParameterUpdatesVersion
            or GetWarningsVersion
            or ContextDocumentIdentityVersion
            or GetSelectionVersion
            or PreviewMepDevicePlacementsVersion
            or RequestMepDevicePlacementReviewVersion
            or ApplyMepDevicePlacementsVersion
            or GetRoomLayoutsVersion
            or GetMepDeviceTypesVersion
            or PreviewRoomPatternPlacementsVersion
            or ReadSpatialRoomsVersion
            or LinkedRoomPatternPreviewVersion;

    public static bool SupportsRequestParameterUpdateReview(int version) =>
        version is RequestParameterUpdateReviewVersion
            or ApplyParameterUpdatesVersion
            or GetWarningsVersion
            or ContextDocumentIdentityVersion
            or GetSelectionVersion
            or PreviewMepDevicePlacementsVersion
            or RequestMepDevicePlacementReviewVersion
            or ApplyMepDevicePlacementsVersion
            or GetRoomLayoutsVersion
            or GetMepDeviceTypesVersion
            or PreviewRoomPatternPlacementsVersion
            or ReadSpatialRoomsVersion
            or LinkedRoomPatternPreviewVersion;

    public static bool SupportsApplyParameterUpdates(int version) =>
        version is ApplyParameterUpdatesVersion
            or GetWarningsVersion
            or ContextDocumentIdentityVersion
            or GetSelectionVersion
            or PreviewMepDevicePlacementsVersion
            or RequestMepDevicePlacementReviewVersion
            or ApplyMepDevicePlacementsVersion
            or GetRoomLayoutsVersion
            or GetMepDeviceTypesVersion
            or PreviewRoomPatternPlacementsVersion
            or ReadSpatialRoomsVersion
            or LinkedRoomPatternPreviewVersion;

    public static bool SupportsGetWarnings(int version) =>
        version is GetWarningsVersion
            or ContextDocumentIdentityVersion
            or GetSelectionVersion
            or PreviewMepDevicePlacementsVersion
            or RequestMepDevicePlacementReviewVersion
            or ApplyMepDevicePlacementsVersion
            or GetRoomLayoutsVersion
            or GetMepDeviceTypesVersion
            or PreviewRoomPatternPlacementsVersion
            or ReadSpatialRoomsVersion
            or LinkedRoomPatternPreviewVersion;

    public static bool SupportsContextDocumentId(int version) =>
        version is ContextDocumentIdentityVersion
            or GetSelectionVersion
            or PreviewMepDevicePlacementsVersion
            or RequestMepDevicePlacementReviewVersion
            or ApplyMepDevicePlacementsVersion
            or GetRoomLayoutsVersion
            or GetMepDeviceTypesVersion
            or PreviewRoomPatternPlacementsVersion
            or ReadSpatialRoomsVersion
            or LinkedRoomPatternPreviewVersion;

    public static bool SupportsActiveViewUnfilteredQuery(int version) =>
        version is ContextDocumentIdentityVersion
            or GetSelectionVersion
            or PreviewMepDevicePlacementsVersion
            or RequestMepDevicePlacementReviewVersion
            or ApplyMepDevicePlacementsVersion
            or GetRoomLayoutsVersion
            or GetMepDeviceTypesVersion
            or PreviewRoomPatternPlacementsVersion
            or ReadSpatialRoomsVersion
            or LinkedRoomPatternPreviewVersion;

    public static bool SupportsGetSelection(int version) =>
        version is GetSelectionVersion
            or PreviewMepDevicePlacementsVersion
            or RequestMepDevicePlacementReviewVersion
            or ApplyMepDevicePlacementsVersion
            or GetRoomLayoutsVersion
            or GetMepDeviceTypesVersion
            or PreviewRoomPatternPlacementsVersion
            or ReadSpatialRoomsVersion
            or LinkedRoomPatternPreviewVersion;

    public static bool SupportsPreviewMepDevicePlacements(int version) =>
        version is PreviewMepDevicePlacementsVersion
            or RequestMepDevicePlacementReviewVersion
            or ApplyMepDevicePlacementsVersion
            or GetRoomLayoutsVersion
            or GetMepDeviceTypesVersion
            or PreviewRoomPatternPlacementsVersion
            or ReadSpatialRoomsVersion
            or LinkedRoomPatternPreviewVersion;

    public static bool SupportsRequestMepDevicePlacementReview(int version) =>
        version is RequestMepDevicePlacementReviewVersion
            or ApplyMepDevicePlacementsVersion
            or GetRoomLayoutsVersion
            or GetMepDeviceTypesVersion
            or PreviewRoomPatternPlacementsVersion
            or ReadSpatialRoomsVersion
            or LinkedRoomPatternPreviewVersion;

    public static bool SupportsApplyMepDevicePlacements(int version) =>
        version is ApplyMepDevicePlacementsVersion
            or GetRoomLayoutsVersion
            or GetMepDeviceTypesVersion
            or PreviewRoomPatternPlacementsVersion
            or ReadSpatialRoomsVersion
            or LinkedRoomPatternPreviewVersion;

    public static bool SupportsGetRoomLayouts(int version) =>
        version is GetRoomLayoutsVersion
            or GetMepDeviceTypesVersion
            or PreviewRoomPatternPlacementsVersion
            or ReadSpatialRoomsVersion
            or LinkedRoomPatternPreviewVersion;

    public static bool SupportsGetMepDeviceTypes(int version) =>
        version is GetMepDeviceTypesVersion
            or PreviewRoomPatternPlacementsVersion
            or ReadSpatialRoomsVersion
            or LinkedRoomPatternPreviewVersion;

    public static bool SupportsPreviewRoomPatternPlacements(int version) =>
        version is PreviewRoomPatternPlacementsVersion
            or ReadSpatialRoomsVersion
            or LinkedRoomPatternPreviewVersion;

    public static bool SupportsReadSpatialRooms(int version) =>
        version is ReadSpatialRoomsVersion
            or LinkedRoomPatternPreviewVersion;

    public static bool SupportsLinkedRoomPatternPreview(int version) =>
        version is LinkedRoomPatternPreviewVersion;
}
