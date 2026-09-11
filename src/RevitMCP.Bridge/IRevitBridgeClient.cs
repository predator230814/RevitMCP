using RevitMCP.Contracts;

namespace RevitMCP.Bridge;

public interface IRevitBridgeClient : IAsyncDisposable
{
    Task<BridgeHandshakeResult> HandshakeAsync(BridgeHandshakeRequest request, CancellationToken cancellationToken);

    Task<GetContextResult> GetContextAsync(GetContextRequest request, TimeSpan timeout, CancellationToken cancellationToken);

    Task<QueryElementsResult> QueryElementsAsync(QueryElementsRequest request, TimeSpan timeout, CancellationToken cancellationToken);

    Task<GetElementsResult> GetElementsAsync(GetElementsRequest request, TimeSpan timeout, CancellationToken cancellationToken);

    Task<DescribeParametersResult> DescribeParametersAsync(
        DescribeParametersRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}
