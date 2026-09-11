using RevitMCP.Contracts;
using Xunit;

namespace RevitMCP.Bridge.Tests;

public sealed class HandshakeServiceTests
{
    [Fact]
    public async Task Highest_common_protocol_version_is_selected()
    {
        var metadata = TestSupport.CreateMetadata(protocolVersions: [3, 2, 1]);
        var service = new BridgeHandshakeService(metadata);

        var result = await service.HandshakeAsync(
            new BridgeHandshakeRequest
            {
                ExpectedInstanceId = metadata.InstanceId,
                SupportedProtocolVersions = [5, 2, 2, 1]
            },
            CancellationToken.None);

        Assert.Equal(2, result.SelectedProtocolVersion);
        Assert.Equal(metadata.InstanceId, result.InstanceId);
    }

    [Fact]
    public async Task Current_peers_select_protocol_5()
    {
        var metadata = TestSupport.CreateMetadata(protocolVersions: BridgeProtocol.SupportedVersions);
        var service = new BridgeHandshakeService(metadata);

        var result = await service.HandshakeAsync(
            new BridgeHandshakeRequest
            {
                ExpectedInstanceId = metadata.InstanceId,
                SupportedProtocolVersions = BridgeProtocol.SupportedVersions
            },
            CancellationToken.None);

        Assert.Equal(BridgeProtocol.CurrentVersion, result.SelectedProtocolVersion);
        Assert.Equal(BridgeProtocol.SupportedVersions, result.SupportedProtocolVersions);
    }

    [Fact]
    public async Task Current_client_falls_back_to_handshake_only_host()
    {
        var metadata = TestSupport.CreateMetadata(protocolVersions: BridgeProtocol.HandshakeOnlyVersions);
        var service = new BridgeHandshakeService(metadata);

        var result = await service.HandshakeAsync(
            new BridgeHandshakeRequest
            {
                ExpectedInstanceId = metadata.InstanceId,
                SupportedProtocolVersions = BridgeProtocol.SupportedVersions
            },
            CancellationToken.None);

        Assert.Equal(BridgeProtocol.HandshakeVersion, result.SelectedProtocolVersion);
        Assert.Equal(BridgeProtocol.HandshakeOnlyVersions, result.SupportedProtocolVersions);
    }

    [Fact]
    public async Task No_common_protocol_returns_incompatible()
    {
        var metadata = TestSupport.CreateMetadata(protocolVersions: [1]);
        var service = new BridgeHandshakeService(metadata);

        var exception = await Assert.ThrowsAsync<BridgeException>(() => service.HandshakeAsync(
            new BridgeHandshakeRequest
            {
                ExpectedInstanceId = metadata.InstanceId,
                SupportedProtocolVersions = [2, 3]
            },
            CancellationToken.None));

        Assert.Equal(BridgeErrorCodes.ProtocolIncompatible, exception.ErrorCode);
    }

    [Fact]
    public async Task Mismatched_instance_id_is_rejected()
    {
        var metadata = TestSupport.CreateMetadata();
        var service = new BridgeHandshakeService(metadata);

        var exception = await Assert.ThrowsAsync<BridgeException>(() => service.HandshakeAsync(
            new BridgeHandshakeRequest
            {
                ExpectedInstanceId = Guid.NewGuid().ToString("D"),
                SupportedProtocolVersions = [1]
            },
            CancellationToken.None));

        Assert.Equal(BridgeErrorCodes.IdentityMismatch, exception.ErrorCode);
    }
}
