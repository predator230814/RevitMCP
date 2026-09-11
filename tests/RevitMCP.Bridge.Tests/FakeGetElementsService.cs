using RevitMCP.Contracts;

namespace RevitMCP.Bridge.Tests;

internal sealed class FakeGetElementsService : IRevitGetElementsService
{
    public int InvokeCount { get; private set; }

    public GetElementsRequest? LastRequest { get; private set; }

    public GetElementsResult? Result { get; set; }

    public Exception? Error { get; set; }

    public TaskCompletionSource<GetElementsResult>? Hold { get; set; }

    public TaskCompletionSource RequestTokenCancelled { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task<GetElementsResult> GetElementsAsync(
        GetElementsRequest request,
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

    public static GetElementsRequest CreateValidRequest()
    {
        return new GetElementsRequest
        {
            DocumentId = "opaque-document-id",
            ElementRefs = ["ref-a"],
            Projection = new GetElementsProjection
            {
                Fields = [GetElementField.Name]
            }
        };
    }

    public static GetElementsResult CreateOkResult()
    {
        return new GetElementsResult
        {
            Context = new GetElementsContext
            {
                InstanceId = "a81e3fd0-6d57-4d27-8dbc-b2f9e98221ac",
                DocumentId = "opaque-document-id"
            },
            Elements =
            [
                new GetElementResult
                {
                    ElementRef = "ref-a",
                    Status = GetElementResultStatus.Ok,
                    Name = ProjectedString.Of("VAV Box 12")
                }
            ]
        };
    }

    public static GetElementsResult CreatePartialResult()
    {
        return new GetElementsResult
        {
            Context = new GetElementsContext
            {
                InstanceId = "a81e3fd0-6d57-4d27-8dbc-b2f9e98221ac",
                DocumentId = "opaque-document-id"
            },
            Elements =
            [
                new GetElementResult
                {
                    ElementRef = "ref-a",
                    Status = GetElementResultStatus.Ok,
                    Name = ProjectedString.Of("VAV Box 12")
                },
                new GetElementResult
                {
                    ElementRef = "not-a-revit-element-ref",
                    Status = GetElementResultStatus.NotFound
                }
            ]
        };
    }

    public static GetElementsResult CreateTriStateResult()
    {
        return new GetElementsResult
        {
            Context = new GetElementsContext
            {
                InstanceId = "a81e3fd0-6d57-4d27-8dbc-b2f9e98221ac",
                DocumentId = "opaque-document-id"
            },
            Elements =
            [
                new GetElementResult
                {
                    ElementRef = "ref-a",
                    Status = GetElementResultStatus.Ok,
                    Name = ProjectedString.Of("VAV Box 12"),
                    CategoryName = ProjectedString.Unavailable,
                    FamilyName = ProjectedString.Omitted,
                    TypeName = ProjectedString.Omitted,
                    LevelName = ProjectedString.Omitted
                }
            ]
        };
    }

    public static GetElementsResult CreateParameterResult()
    {
        return new GetElementsResult
        {
            Context = new GetElementsContext
            {
                InstanceId = "a81e3fd0-6d57-4d27-8dbc-b2f9e98221ac",
                DocumentId = "opaque-document-id"
            },
            Elements =
            [
                new GetElementResult
                {
                    ElementRef = "ref-a",
                    Status = GetElementResultStatus.Ok,
                    Parameters =
                    [
                        new GetElementParameter
                        {
                            Name = "Flow",
                            Source = GetElementParameterSource.Instance,
                            ValueText = "850 CFM",
                            ValueTruncated = false
                        },
                        new GetElementParameter
                        {
                            Name = "Type Mark",
                            Source = GetElementParameterSource.Type,
                            ValueText = null,
                            ValueTruncated = false
                        }
                    ],
                    ParametersTruncated = false
                }
            ]
        };
    }
}
