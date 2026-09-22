using RevitMCP.Contracts;

namespace RevitMCP.Bridge.Tests;

internal sealed class FakeGetParameterValuesService : IRevitGetParameterValuesService
{
    public int InvokeCount { get; private set; }

    public GetParameterValuesRequest? LastRequest { get; private set; }

    public GetParameterValuesResult? Result { get; set; }

    public Exception? Error { get; set; }

    public TaskCompletionSource<GetParameterValuesResult>? Hold { get; set; }

    public TaskCompletionSource RequestTokenCancelled { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task<GetParameterValuesResult> GetParameterValuesAsync(
        GetParameterValuesRequest request,
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

        return Result ?? CreateOkResult();
    }

    public static GetParameterValuesRequest CreateValidRequest()
    {
        return new GetParameterValuesRequest
        {
            DocumentId = "opaque-document-id",
            Reads =
            [
                new GetParameterValueRead
                {
                    ElementRef = "ref-a",
                    ParameterRef = "opaque-parameter-ref"
                }
            ]
        };
    }

    public static GetParameterValuesResult CreateOkResult()
    {
        return new GetParameterValuesResult
        {
            Context = new DescribeParametersContext
            {
                InstanceId = "a81e3fd0-6d57-4d27-8dbc-b2f9e98221ac",
                DocumentId = "opaque-document-id"
            },
            Items =
            [
                new GetParameterValueItem
                {
                    ElementRef = "ref-a",
                    ParameterRef = "opaque-parameter-ref",
                    Status = GetParameterValueStatus.Ok,
                    DataType = new DescribeParameterDataType
                    {
                        Kind = DescribeParameterDataTypeKind.Spec
                    },
                    HasValue = true,
                    Value = new GetParameterIntegerValue { Value = 4 }
                }
            ]
        };
    }

    public static GetParameterValuesResult CreateItemStatusResult(GetParameterValueStatus status)
    {
        return new GetParameterValuesResult
        {
            Context = new DescribeParametersContext
            {
                InstanceId = "a81e3fd0-6d57-4d27-8dbc-b2f9e98221ac",
                DocumentId = "opaque-document-id"
            },
            Items =
            [
                new GetParameterValueItem
                {
                    ElementRef = "ref-a",
                    ParameterRef = "opaque-parameter-ref",
                    Status = status
                }
            ]
        };
    }
}
