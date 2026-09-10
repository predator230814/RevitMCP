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

    public Task<GetContextResult> GetContextAsync(GetContextRequest request, TimeSpan timeout, CancellationToken cancellationToken)
    {
        _ = request;
        _ = timeout;
        _ = cancellationToken;
        throw new NotSupportedException("This fake client does not implement revit.get_context.");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
