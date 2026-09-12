using RevitMCP.Contracts;

namespace RevitMCP.Bridge.Tests;

internal sealed class FakeDescribeParametersService : IRevitDescribeParametersService
{
    public int InvokeCount { get; private set; }

    public DescribeParametersRequest? LastRequest { get; private set; }

    public DescribeParametersResult? Result { get; set; }

    public Exception? Error { get; set; }

    public TaskCompletionSource<DescribeParametersResult>? Hold { get; set; }

    public TaskCompletionSource RequestTokenCancelled { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task<DescribeParametersResult> DescribeParametersAsync(
        DescribeParametersRequest request,
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

    public static DescribeParametersRequest CreateValidRequest()
    {
        return new DescribeParametersRequest
        {
            DocumentId = "opaque-document-id",
            ElementRefs = ["ref-a"],
            Source = DescribeParameterSource.Both,
            Limit = 50
        };
    }

    public static DescribeParametersResult CreateOkResult()
    {
        return new DescribeParametersResult
        {
            Context = new DescribeParametersContext
            {
                InstanceId = "a81e3fd0-6d57-4d27-8dbc-b2f9e98221ac",
                DocumentId = "opaque-document-id"
            },
            Elements =
            [
                new DescribeParameterElementResult
                {
                    ElementRef = "ref-a",
                    Status = GetElementResultStatus.Ok
                }
            ],
            MatchedCount = 1,
            Truncated = false,
            Parameters =
            [
                new DescribeParameterDescriptor
                {
                    ParameterRef = "opaque-parameter-ref",
                    Name = "Flow",
                    Source = GetElementParameterSource.Instance,
                    Identity = new DescribeParameterIdentity
                    {
                        Kind = DescribeParameterIdentityKind.BuiltIn,
                        ParameterTypeId = "autodesk.revit.parameter:hvacAirflow"
                    },
                    DataType = new DescribeParameterDataType
                    {
                        Kind = DescribeParameterDataTypeKind.MeasurableSpec,
                        ForgeTypeId = "autodesk.spec.aec:airflow"
                    },
                    PresentOnCount = 1,
                    ReadOnlyOnCount = 0
                }
            ]
        };
    }
}
