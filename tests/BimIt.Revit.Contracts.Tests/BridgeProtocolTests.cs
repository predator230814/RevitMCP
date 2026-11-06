using Xunit;

namespace BimIt.Revit.Contracts.Tests;

public sealed class BridgeProtocolTests
{
    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(4, true)]
    [InlineData(5, true)]
    [InlineData(6, true)]
    [InlineData(7, true)]
    [InlineData(8, true)]
    [InlineData(9, true)]
    [InlineData(10, true)]
    [InlineData(11, true)]
    [InlineData(12, true)]
    [InlineData(13, true)]
    [InlineData(14, true)]
    [InlineData(15, true)]
    [InlineData(16, true)]
    [InlineData(17, true)]
    [InlineData(18, true)]
    [InlineData(19, true)]
    [InlineData(20, true)]
    [InlineData(21, true)]
    [InlineData(22, true)]
    [InlineData(23, true)]
    [InlineData(24, false)]
    public void Get_context_support_is_an_explicit_set(int version, bool expected)
    {
        Assert.Equal(expected, BridgeProtocol.SupportsGetContext(version));
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, true)]
    [InlineData(4, true)]
    [InlineData(5, true)]
    [InlineData(6, true)]
    [InlineData(7, true)]
    [InlineData(8, true)]
    [InlineData(9, true)]
    [InlineData(10, true)]
    [InlineData(11, true)]
    [InlineData(12, true)]
    [InlineData(13, true)]
    [InlineData(14, true)]
    [InlineData(15, true)]
    [InlineData(16, true)]
    [InlineData(17, true)]
    [InlineData(18, true)]
    [InlineData(19, true)]
    [InlineData(20, true)]
    [InlineData(21, true)]
    [InlineData(22, true)]
    [InlineData(23, true)]
    [InlineData(24, false)]
    public void Query_elements_support_is_an_explicit_set(int version, bool expected)
    {
        Assert.Equal(expected, BridgeProtocol.SupportsQueryElements(version));
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(4, true)]
    [InlineData(5, true)]
    [InlineData(6, true)]
    [InlineData(7, true)]
    [InlineData(8, true)]
    [InlineData(9, true)]
    [InlineData(10, true)]
    [InlineData(11, true)]
    [InlineData(12, true)]
    [InlineData(13, true)]
    [InlineData(14, true)]
    [InlineData(15, true)]
    [InlineData(16, true)]
    [InlineData(17, true)]
    [InlineData(18, true)]
    [InlineData(19, true)]
    [InlineData(20, true)]
    [InlineData(21, true)]
    [InlineData(22, true)]
    [InlineData(23, true)]
    [InlineData(24, false)]
    public void Get_elements_support_is_an_explicit_set(int version, bool expected)
    {
        Assert.Equal(expected, BridgeProtocol.SupportsGetElements(version));
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(4, false)]
    [InlineData(5, true)]
    [InlineData(6, true)]
    [InlineData(7, true)]
    [InlineData(8, true)]
    [InlineData(9, true)]
    [InlineData(10, true)]
    [InlineData(11, true)]
    [InlineData(12, true)]
    [InlineData(13, true)]
    [InlineData(14, true)]
    [InlineData(15, true)]
    [InlineData(16, true)]
    [InlineData(17, true)]
    [InlineData(18, true)]
    [InlineData(19, true)]
    [InlineData(20, true)]
    [InlineData(21, true)]
    [InlineData(22, true)]
    [InlineData(23, true)]
    [InlineData(24, false)]
    public void Describe_parameters_support_is_an_explicit_set(int version, bool expected)
    {
        Assert.Equal(expected, BridgeProtocol.SupportsDescribeParameters(version));
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(4, false)]
    [InlineData(5, false)]
    [InlineData(6, true)]
    [InlineData(7, true)]
    [InlineData(8, true)]
    [InlineData(9, true)]
    [InlineData(10, true)]
    [InlineData(11, true)]
    [InlineData(12, true)]
    [InlineData(13, true)]
    [InlineData(14, true)]
    [InlineData(15, true)]
    [InlineData(16, true)]
    [InlineData(17, true)]
    [InlineData(18, true)]
    [InlineData(19, true)]
    [InlineData(20, true)]
    [InlineData(21, true)]
    [InlineData(22, true)]
    [InlineData(23, true)]
    [InlineData(24, false)]
    public void Get_parameter_values_support_is_an_explicit_set(int version, bool expected)
    {
        Assert.Equal(expected, BridgeProtocol.SupportsGetParameterValues(version));
    }

    [Fact]
    public void Current_supported_versions_are_explicit_and_ordered()
    {
        Assert.Equal(23, BridgeProtocol.CurrentVersion);
        Assert.Equal(new[] { 1 }, BridgeProtocol.HandshakeOnlyVersions);
        Assert.Equal(new[] { 2, 1 }, BridgeProtocol.GetContextVersions);
        Assert.Equal(new[] { 3, 2, 1 }, BridgeProtocol.QueryElementsVersions);
        Assert.Equal(new[] { 4, 3, 2, 1 }, BridgeProtocol.GetElementsVersions);
        Assert.Equal(new[] { 5, 4, 3, 2, 1 }, BridgeProtocol.DescribeParametersVersions);
        Assert.Equal(new[] { 6, 5, 4, 3, 2, 1 }, BridgeProtocol.GetParameterValuesVersions);
        Assert.Equal(new[] { 7, 6, 5, 4, 3, 2, 1 }, BridgeProtocol.GetMepTopologyVersions);
        Assert.Equal(new[] { 8, 7, 6, 5, 4, 3, 2, 1 }, BridgeProtocol.PreviewParameterUpdatesVersions);
        Assert.Equal(new[] { 9, 8, 7, 6, 5, 4, 3, 2, 1 }, BridgeProtocol.RequestParameterUpdateReviewVersions);
        Assert.Equal(new[] { 10, 9, 8, 7, 6, 5, 4, 3, 2, 1 }, BridgeProtocol.ApplyParameterUpdatesVersions);
        Assert.Equal(new[] { 12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1 }, BridgeProtocol.ContextDocumentIdentityVersions);
        Assert.Equal(new[] { 13, 12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1 }, BridgeProtocol.GetSelectionVersions);
        Assert.Equal(new[] { 14, 13, 12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1 }, BridgeProtocol.PreviewMepDevicePlacementsVersions);
        Assert.Equal(new[] { 15, 14, 13, 12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1 }, BridgeProtocol.RequestMepDevicePlacementReviewVersions);
        Assert.Equal(new[] { 16, 15, 14, 13, 12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1 }, BridgeProtocol.ApplyMepDevicePlacementsVersions);
        Assert.Equal(new[] { 17, 16, 15, 14, 13, 12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1 }, BridgeProtocol.GetRoomLayoutsVersions);
        Assert.Equal(new[] { 18, 17, 16, 15, 14, 13, 12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1 }, BridgeProtocol.GetMepDeviceTypesVersions);
        Assert.Equal(new[] { 19, 18, 17, 16, 15, 14, 13, 12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1 }, BridgeProtocol.PreviewRoomPatternPlacementsVersions);
        Assert.Equal(new[] { 20, 19, 18, 17, 16, 15, 14, 13, 12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1 }, BridgeProtocol.ReadSpatialRoomsVersions);
        Assert.Equal(new[] { 21, 20, 19, 18, 17, 16, 15, 14, 13, 12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1 }, BridgeProtocol.LinkedRoomPatternPreviewVersions);
        Assert.Equal(new[] { 22, 21, 20, 19, 18, 17, 16, 15, 14, 13, 12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1 }, BridgeProtocol.QueryFamilyTypesVersions);
        Assert.Equal(new[] { 23, 22, 21, 20, 19, 18, 17, 16, 15, 14, 13, 12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1 }, BridgeProtocol.QueryParameterBindingsVersions);
        Assert.Equal(BridgeProtocol.QueryParameterBindingsVersions, BridgeProtocol.SupportedVersions);
        Assert.Equal(new[] { BridgeProtocol.QueryParameterBindingsVersion }.Concat(BridgeProtocol.QueryFamilyTypesVersions), BridgeProtocol.QueryParameterBindingsVersions);
        Assert.True(BridgeProtocol.SupportsQueryFamilyTypes(22));
        Assert.False(BridgeProtocol.SupportsQueryFamilyTypes(21));
        Assert.True(BridgeProtocol.SupportsQueryFamilyTypes(23)); Assert.False(BridgeProtocol.SupportsQueryFamilyTypes(24));
        Assert.True(BridgeProtocol.SupportsLinkedRoomPatternPreview(21));
        Assert.True(BridgeProtocol.SupportsLinkedRoomPatternPreview(22));
        Assert.False(BridgeProtocol.SupportsLinkedRoomPatternPreview(20));
        Assert.False(BridgeProtocol.SupportsLinkedRoomPatternPreview(19));
        Assert.True(BridgeProtocol.SupportsLinkedRoomPatternPreview(23)); Assert.False(BridgeProtocol.SupportsLinkedRoomPatternPreview(24));
        Assert.True(BridgeProtocol.SupportsPreviewRoomPatternPlacements(21));
        Assert.True(BridgeProtocol.SupportsPreviewRoomPatternPlacements(22));
        Assert.True(BridgeProtocol.SupportsPreviewRoomPatternPlacements(23)); Assert.False(BridgeProtocol.SupportsPreviewRoomPatternPlacements(24));
        Assert.True(BridgeProtocol.SupportsReadSpatialRooms(20));
        Assert.False(BridgeProtocol.SupportsReadSpatialRooms(19));
        Assert.True(BridgeProtocol.SupportsReadSpatialRooms(21));
        Assert.True(BridgeProtocol.SupportsReadSpatialRooms(22));
        Assert.True(BridgeProtocol.SupportsReadSpatialRooms(23)); Assert.False(BridgeProtocol.SupportsReadSpatialRooms(24));
        Assert.False(BridgeProtocol.SupportsPreviewMepDevicePlacements(13));
        Assert.True(BridgeProtocol.SupportsPreviewMepDevicePlacements(14));
        Assert.False(BridgeProtocol.SupportsRequestMepDevicePlacementReview(14));
        Assert.True(BridgeProtocol.SupportsRequestMepDevicePlacementReview(15));
        Assert.False(BridgeProtocol.SupportsApplyMepDevicePlacements(15));
        Assert.True(BridgeProtocol.SupportsApplyMepDevicePlacements(16));
        Assert.True(BridgeProtocol.SupportsApplyMepDevicePlacements(17));
        Assert.True(BridgeProtocol.SupportsGetContext(17));
        Assert.True(BridgeProtocol.SupportsGetRoomLayouts(17));
        Assert.True(BridgeProtocol.SupportsGetRoomLayouts(18));
        Assert.True(BridgeProtocol.SupportsGetRoomLayouts(19));
        Assert.False(BridgeProtocol.SupportsGetRoomLayouts(16));
        Assert.True(BridgeProtocol.SupportsGetRoomLayouts(20)); Assert.True(BridgeProtocol.SupportsGetRoomLayouts(21)); Assert.True(BridgeProtocol.SupportsGetRoomLayouts(22)); Assert.True(BridgeProtocol.SupportsGetRoomLayouts(23)); Assert.False(BridgeProtocol.SupportsGetRoomLayouts(24));
        Assert.False(BridgeProtocol.SupportsGetMepDeviceTypes(17));
        Assert.True(BridgeProtocol.SupportsGetMepDeviceTypes(18));
        Assert.True(BridgeProtocol.SupportsGetMepDeviceTypes(19));
        Assert.False(BridgeProtocol.SupportsPreviewRoomPatternPlacements(18));
        Assert.True(BridgeProtocol.SupportsPreviewRoomPatternPlacements(19));
        Assert.True(BridgeProtocol.SupportsGetContext(18));
        Assert.True(BridgeProtocol.SupportsGetContext(19));
        Assert.True(BridgeProtocol.SupportsGetContext(20)); Assert.True(BridgeProtocol.SupportsGetContext(21)); Assert.True(BridgeProtocol.SupportsGetContext(22)); Assert.True(BridgeProtocol.SupportsGetContext(23)); Assert.False(BridgeProtocol.SupportsGetContext(24));
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(4, false)]
    [InlineData(5, false)]
    [InlineData(6, false)]
    [InlineData(7, true)]
    [InlineData(8, true)]
    [InlineData(9, true)]
    [InlineData(10, true)]
    [InlineData(11, true)]
    [InlineData(12, true)]
    [InlineData(13, true)]
    [InlineData(14, true)]
    [InlineData(15, true)]
    [InlineData(16, true)]
    [InlineData(17, true)]
    [InlineData(18, true)]
    [InlineData(19, true)]
    [InlineData(20, true)]
    [InlineData(21, true)]
    [InlineData(22, true)]
    [InlineData(23, true)]
    [InlineData(24, false)]
    public void Get_mep_topology_support_includes_v7_through_v11(int version, bool expected)
    {
        Assert.Equal(expected, BridgeProtocol.SupportsGetMepTopology(version));
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(7, false)]
    [InlineData(8, true)]
    [InlineData(9, true)]
    [InlineData(10, true)]
    [InlineData(11, true)]
    [InlineData(12, true)]
    [InlineData(13, true)]
    [InlineData(14, true)]
    [InlineData(15, true)]
    [InlineData(16, true)]
    [InlineData(17, true)]
    [InlineData(18, true)]
    [InlineData(19, true)]
    [InlineData(20, true)]
    [InlineData(21, true)]
    [InlineData(22, true)]
    [InlineData(23, true)]
    [InlineData(24, false)]
    public void Preview_parameter_updates_support_includes_v8_through_v11(int version, bool expected)
    {
        Assert.Equal(expected, BridgeProtocol.SupportsPreviewParameterUpdates(version));
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(4, false)]
    [InlineData(5, false)]
    [InlineData(6, false)]
    [InlineData(7, false)]
    [InlineData(8, false)]
    [InlineData(9, true)]
    [InlineData(10, true)]
    [InlineData(11, true)]
    [InlineData(12, true)]
    [InlineData(13, true)]
    [InlineData(14, true)]
    [InlineData(15, true)]
    [InlineData(16, true)]
    [InlineData(17, true)]
    [InlineData(18, true)]
    [InlineData(19, true)]
    [InlineData(20, true)]
    [InlineData(21, true)]
    [InlineData(22, true)]
    [InlineData(23, true)]
    [InlineData(24, false)]
    public void Request_parameter_update_review_support_is_protocol_v9_through_v11(int version, bool expected)
    {
        Assert.Equal(expected, BridgeProtocol.SupportsRequestParameterUpdateReview(version));
    }

    [Theory]
    [InlineData(10, false)]
    [InlineData(11, true)]
    [InlineData(12, true)]
    [InlineData(13, true)]
    [InlineData(14, true)]
    [InlineData(15, true)]
    [InlineData(16, true)]
    [InlineData(17, true)]
    [InlineData(18, true)]
    [InlineData(19, true)]
    [InlineData(20, true)]
    [InlineData(21, true)]
    [InlineData(22, true)]
    [InlineData(23, true)]
    [InlineData(24, false)]
    public void Get_warnings_support_is_protocol_v11_and_v12(int version, bool expected)
    {
        Assert.Equal(expected, BridgeProtocol.SupportsGetWarnings(version));
    }

    [Theory]
    [InlineData(11, false)]
    [InlineData(12, true)]
    [InlineData(13, true)]
    [InlineData(14, true)]
    [InlineData(15, true)]
    [InlineData(16, true)]
    [InlineData(17, true)]
    [InlineData(18, true)]
    [InlineData(19, true)]
    [InlineData(20, true)]
    [InlineData(21, true)]
    [InlineData(22, true)]
    [InlineData(23, true)]
    [InlineData(24, false)]
    public void Context_document_id_is_a_v12_guarantee(int version, bool expected)
    {
        Assert.Equal(expected, BridgeProtocol.SupportsContextDocumentId(version));
    }

    [Theory]
    [InlineData(11, false)]
    [InlineData(12, true)]
    [InlineData(13, true)]
    [InlineData(14, true)]
    [InlineData(15, true)]
    [InlineData(16, true)]
    [InlineData(17, true)]
    [InlineData(18, true)]
    [InlineData(19, true)]
    [InlineData(20, true)]
    [InlineData(21, true)]
    [InlineData(22, true)]
    [InlineData(23, true)]
    [InlineData(24, false)]
    public void Active_view_unfiltered_query_is_a_v12_guarantee(int version, bool expected)
    {
        Assert.Equal(expected, BridgeProtocol.SupportsActiveViewUnfilteredQuery(version));
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(12, false)]
    [InlineData(13, true)]
    [InlineData(14, true)]
    [InlineData(15, true)]
    [InlineData(16, true)]
    [InlineData(17, true)]
    [InlineData(18, true)]
    [InlineData(19, true)]
    [InlineData(20, true)]
    [InlineData(21, true)]
    [InlineData(22, true)]
    [InlineData(23, true)]
    [InlineData(24, false)]
    public void Get_selection_support_is_protocol_v13_only(int version, bool expected)
    {
        Assert.Equal(expected, BridgeProtocol.SupportsGetSelection(version));
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(21, false)]
    [InlineData(22, false)]
    [InlineData(23, true)]
    [InlineData(24, false)]
    [InlineData(int.MaxValue, false)]
    public void Query_parameter_bindings_support_is_exactly_v23(int version, bool expected)
    {
        Assert.Equal(expected, BridgeProtocol.SupportsQueryParameterBindings(version));
    }

    [Fact]
    public void Protocol_23_preserves_every_v22_capability_and_unknown_24_supports_nothing()
    {
        var supports = typeof(BridgeProtocol)
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(method => method.Name.StartsWith("Supports", StringComparison.Ordinal)
                && method.ReturnType == typeof(bool)
                && method.GetParameters() is [{ ParameterType: var type }] && type == typeof(int))
            .ToArray();
        Assert.Contains(supports, method => method.Name == nameof(BridgeProtocol.SupportsQueryParameterBindings));
        Assert.Contains(supports, method => method.Name == nameof(BridgeProtocol.SupportsQueryFamilyTypes));

        foreach (var method in supports)
        {
            bool At(int version) => (bool)method.Invoke(null, [version])!;
            if (method.Name == nameof(BridgeProtocol.SupportsQueryParameterBindings))
            {
                Assert.False(At(22));
                Assert.True(At(23));
            }
            else
            {
                Assert.True(At(22), method.Name + " must support 22");
                Assert.True(At(23), method.Name + " must be preserved on 23");
            }

            Assert.False(At(24), method.Name + " must not support unknown 24");
            Assert.False(At(0), method.Name + " must not support 0");
        }
    }
}
