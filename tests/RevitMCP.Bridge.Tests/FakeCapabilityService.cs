using RevitMCP.Contracts;

namespace RevitMCP.Bridge.Tests;

internal sealed class FakeCapabilityService : IRevitCapabilityService
{
    public int InvokeCount { get; private set; }

    public GetContextRequest? LastRequest { get; private set; }

    public GetContextResult? Result { get; set; }

    public Exception? Error { get; set; }

    public TaskCompletionSource<GetContextResult>? Hold { get; set; }

    public async Task<GetContextResult> GetContextAsync(GetContextRequest request, CancellationToken cancellationToken)
    {
        InvokeCount++;
        LastRequest = request;

        if (Hold is not null)
        {
            return await Hold.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        if (Error is not null)
        {
            throw Error;
        }

        return Result ?? CreateZeroDocumentResult();
    }

    public static GetContextResult CreateZeroDocumentResult()
    {
        return new GetContextResult
        {
            Instance = new GetContextInstance
            {
                InstanceId = "a81e3fd0-6d57-4d27-8dbc-b2f9e98221ac",
                RevitVersion = "2026",
                RevitBuild = "26.5.0.55"
            },
            Document = null,
            ActiveView = null,
            Selection = new GetContextSelection { Count = 0 }
        };
    }

    public static GetContextResult CreateProjectResult()
    {
        return new GetContextResult
        {
            Instance = new GetContextInstance
            {
                InstanceId = "a81e3fd0-6d57-4d27-8dbc-b2f9e98221ac",
                RevitVersion = "2026",
                RevitBuild = "26.5.0.55"
            },
            Document = new GetContextDocument
            {
                Title = "Hospital-MEP",
                Kind = GetContextDocumentKind.Project,
                IsWorkshared = true,
                IsModelInCloud = true,
                IsReadOnly = false,
                IsModified = true
            },
            ActiveView = new GetContextActiveView
            {
                ElementId = "184392",
                Name = "Level 02 - HVAC",
                ViewType = "FloorPlan"
            },
            Selection = new GetContextSelection { Count = 12 }
        };
    }
}
