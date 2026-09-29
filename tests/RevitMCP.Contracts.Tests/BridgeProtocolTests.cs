using Xunit;

namespace RevitMCP.Contracts.Tests;

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
    [InlineData(9, false)]
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
    [InlineData(9, false)]
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
    [InlineData(9, false)]
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
    [InlineData(9, false)]
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
    [InlineData(9, false)]
    public void Get_parameter_values_support_is_an_explicit_set(int version, bool expected)
    {
        Assert.Equal(expected, BridgeProtocol.SupportsGetParameterValues(version));
    }

    [Fact]
    public void Current_supported_versions_are_explicit_and_ordered()
    {
        Assert.Equal(8, BridgeProtocol.CurrentVersion);
        Assert.Equal(new[] { 1 }, BridgeProtocol.HandshakeOnlyVersions);
        Assert.Equal(new[] { 2, 1 }, BridgeProtocol.GetContextVersions);
        Assert.Equal(new[] { 3, 2, 1 }, BridgeProtocol.QueryElementsVersions);
        Assert.Equal(new[] { 4, 3, 2, 1 }, BridgeProtocol.GetElementsVersions);
        Assert.Equal(new[] { 5, 4, 3, 2, 1 }, BridgeProtocol.DescribeParametersVersions);
        Assert.Equal(new[] { 6, 5, 4, 3, 2, 1 }, BridgeProtocol.GetParameterValuesVersions);
        Assert.Equal(new[] { 7, 6, 5, 4, 3, 2, 1 }, BridgeProtocol.GetMepTopologyVersions);
        Assert.Equal(new[] { 8, 7, 6, 5, 4, 3, 2, 1 }, BridgeProtocol.SupportedVersions);
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
    [InlineData(9, false)]
    public void Get_mep_topology_support_includes_v7_and_v8_only(int version, bool expected)
    {
        Assert.Equal(expected, BridgeProtocol.SupportsGetMepTopology(version));
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(7, false)]
    [InlineData(8, true)]
    [InlineData(9, false)]
    public void Preview_parameter_updates_support_is_protocol_v8_only(int version, bool expected)
    {
        Assert.Equal(expected, BridgeProtocol.SupportsPreviewParameterUpdates(version));
    }
}
