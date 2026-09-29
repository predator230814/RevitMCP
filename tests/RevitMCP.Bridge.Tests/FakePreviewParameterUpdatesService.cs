using RevitMCP.Contracts;

namespace RevitMCP.Bridge.Tests;

internal sealed class FakePreviewParameterUpdatesService : IRevitPreviewParameterUpdatesService
{
    public int InvokeCount { get; private set; }

    public PreviewParameterUpdatesRequest? LastRequest { get; private set; }

    public PreviewParameterUpdatesResult? Result { get; set; }

    public Exception? Error { get; set; }

    public TaskCompletionSource<PreviewParameterUpdatesResult>? Hold { get; set; }

    public TaskCompletionSource RequestTokenCancelled { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task<PreviewParameterUpdatesResult> PreviewParameterUpdatesAsync(
        PreviewParameterUpdatesRequest request,
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

        return Result ?? CreateReadyResult();
    }

    public static PreviewParameterUpdatesRequest CreateRequest()
    {
        return new PreviewParameterUpdatesRequest
        {
            DocumentId = "opaque-document-id",
            Updates =
            [
                new PreviewParameterUpdate
                {
                    ElementRef = "element-ref",
                    ParameterRef = "parameter-ref",
                    Value = new PreviewParameterStringValue { Value = "proposed" }
                }
            ]
        };
    }

    public static PreviewParameterUpdatesResult CreateReadyResult()
    {
        return new PreviewParameterUpdatesResult
        {
            Context = CreateContext(),
            Ready = true,
            Items = [CreateItem(PreviewParameterUpdateStatus.Ok)],
            IntentRef = "intent-ref",
            IntentFingerprint = "fingerprint",
            ExpiresAt = DateTimeOffset.Parse("2026-09-29T23:00:00Z")
        };
    }

    public static PreviewParameterUpdatesResult CreateNotReadyResult()
    {
        return new PreviewParameterUpdatesResult
        {
            Context = CreateContext(),
            Ready = false,
            Items = [CreateItem(PreviewParameterUpdateStatus.ParameterNotWritable)]
        };
    }

    private static DescribeParametersContext CreateContext()
    {
        return new DescribeParametersContext
        {
            InstanceId = "instance",
            DocumentId = "opaque-document-id"
        };
    }

    private static PreviewParameterUpdateItem CreateItem(PreviewParameterUpdateStatus status)
    {
        return new PreviewParameterUpdateItem
        {
            ElementRef = "element-ref",
            ParameterRef = "parameter-ref",
            Status = status
        };
    }
}
