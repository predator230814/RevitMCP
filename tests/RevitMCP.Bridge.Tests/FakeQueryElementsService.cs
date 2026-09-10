using RevitMCP.Contracts;

namespace RevitMCP.Bridge.Tests;

internal sealed class FakeQueryElementsService : IRevitQueryElementsService
{
    public int InvokeCount { get; private set; }

    public QueryElementsRequest? LastRequest { get; private set; }

    public QueryElementsResult? Result { get; set; }

    public Exception? Error { get; set; }

    public TaskCompletionSource<QueryElementsResult>? Hold { get; set; }

    public TaskCompletionSource RequestTokenCancelled { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task<QueryElementsResult> QueryElementsAsync(
        QueryElementsRequest request,
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

        return Result ?? CreateEmptyResult();
    }

    public static QueryElementsResult CreateEmptyResult()
    {
        return new QueryElementsResult
        {
            Context = new QueryElementsContext
            {
                InstanceId = "a81e3fd0-6d57-4d27-8dbc-b2f9e98221ac",
                DocumentId = "opaque-document-id"
            },
            MatchedCount = 0,
            Truncated = false,
            ElementRefs = []
        };
    }

    public static QueryElementsResult CreateBoundedResult()
    {
        return new QueryElementsResult
        {
            Context = new QueryElementsContext
            {
                InstanceId = "a81e3fd0-6d57-4d27-8dbc-b2f9e98221ac",
                DocumentId = "opaque-document-id"
            },
            MatchedCount = 2,
            Truncated = true,
            ElementRefs = ["ref-a"]
        };
    }

    public static QueryElementsRequest CreateValidRequest()
    {
        return new QueryElementsRequest
        {
            Scope = QueryScope.Document,
            Filters = new QueryElementFilters { CategoryNames = ["Mechanical Equipment"] },
            Limit = 50
        };
    }
}
