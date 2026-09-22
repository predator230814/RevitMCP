using RevitMCP.Contracts;

namespace RevitMCP.Bridge.Tests;

internal sealed class FakeGetMepTopologyService : IRevitGetMepTopologyService
{
    public int InvokeCount { get; private set; }

    public GetMepTopologyRequest? LastRequest { get; private set; }

    public GetMepTopologyResult? Result { get; set; }

    public Exception? Error { get; set; }

    public TaskCompletionSource<GetMepTopologyResult>? Hold { get; set; }

    public TaskCompletionSource RequestTokenCancelled { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task<GetMepTopologyResult> GetMepTopologyAsync(
        GetMepTopologyRequest request,
        CancellationToken cancellationToken)
    {
        InvokeCount++;
        LastRequest = request;
        cancellationToken.Register(() => RequestTokenCancelled.TrySetResult());
        if (Hold is not null)
        {
            return await Hold.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        if (Error is not null)
        {
            throw Error;
        }

        return Result ?? CreateResult();
    }

    public static GetMepTopologyRequest CreateRequest()
    {
        return new GetMepTopologyRequest
        {
            DocumentId = "opaque-document-id",
            SeedElementRefs = ["seed"]
        };
    }

    public static GetMepTopologyResult CreateResult()
    {
        return new GetMepTopologyResult
        {
            Context = new GetMepTopologyContext
            {
                InstanceId = "instance",
                DocumentId = "opaque-document-id"
            },
            Seeds = [new GetMepTopologySeed { ElementRef = "seed", Status = MepTopologySeedStatus.Ok }],
            Nodes = [new GetMepTopologyNode { ElementRef = "seed", Depth = 0 }],
            Edges = [],
            Truncated = false,
            TruncationReasons = []
        };
    }
}
