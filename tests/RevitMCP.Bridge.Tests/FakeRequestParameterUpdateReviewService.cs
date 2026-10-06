using RevitMCP.Contracts;

namespace RevitMCP.Bridge.Tests;

internal sealed class FakeRequestParameterUpdateReviewService : IRevitRequestParameterUpdateReviewService
{
    public int InvokeCount { get; private set; }

    public RequestParameterUpdateReviewRequest? LastRequest { get; private set; }

    public RequestParameterUpdateReviewResult? Result { get; set; }

    public Exception? Error { get; set; }

    public TaskCompletionSource<RequestParameterUpdateReviewResult>? Hold { get; set; }

    public TaskCompletionSource RequestTokenCancelled { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task<RequestParameterUpdateReviewResult> RequestParameterUpdateReviewAsync(
        RequestParameterUpdateReviewRequest request,
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

        return Result ?? new RequestParameterUpdateReviewResult
        {
            Status = RequestParameterUpdateReviewStatus.Started
        };
    }
}
