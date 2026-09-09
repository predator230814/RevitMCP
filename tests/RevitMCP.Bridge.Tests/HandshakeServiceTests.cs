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
