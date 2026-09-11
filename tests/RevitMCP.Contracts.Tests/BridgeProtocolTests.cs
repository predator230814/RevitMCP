using Xunit;

namespace RevitMCP.Contracts.Tests;

public sealed class BridgeProtocolTests
{
    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(4, true)]
    [InlineData(5, false)]
    public void Get_context_support_is_an_explicit_set(int version, bool expected)
    {
        Assert.Equal(expected, BridgeProtocol.SupportsGetContext(version));
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, true)]
    [InlineData(4, true)]
    [InlineData(5, false)]
    public void Query_elements_support_is_an_explicit_set(int version, bool expected)
    {
        Assert.Equal(expected, BridgeProtocol.SupportsQueryElements(version));
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(4, true)]
    [InlineData(5, false)]
    public void Get_elements_support_is_an_explicit_set(int version, bool expected)
    {
        Assert.Equal(expected, BridgeProtocol.SupportsGetElements(version));
    }

    [Fact]
    public void Current_supported_versions_are_explicit_and_ordered()
    {
        Assert.Equal(4, BridgeProtocol.CurrentVersion);
        Assert.Equal(new[] { 1 }, BridgeProtocol.HandshakeOnlyVersions);
        Assert.Equal(new[] { 2, 1 }, BridgeProtocol.GetContextVersions);
        Assert.Equal(new[] { 3, 2, 1 }, BridgeProtocol.QueryElementsVersions);
        Assert.Equal(new[] { 4, 3, 2, 1 }, BridgeProtocol.SupportedVersions);
    }
}
