using RevitMCP.Contracts;

namespace RevitMCP.Bridge.Tests;

internal sealed class FakeBridgeClient : IRevitBridgeClient
{
    private readonly Func<BridgeHandshakeRequest, CancellationToken, Task<BridgeHandshakeResult>> _handshake;

    public FakeBridgeClient(Func<BridgeHandshakeRequest, CancellationToken, Task<BridgeHandshakeResult>> handshake)
    {
        _handshake = handshake;
    }

    public Task<BridgeHandshakeResult> HandshakeAsync(BridgeHandshakeRequest request, CancellationToken cancellationToken)
    {
        return _handshake(request, cancellationToken);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
