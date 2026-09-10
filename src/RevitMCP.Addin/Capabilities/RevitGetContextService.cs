using Autodesk.Revit.UI;
using RevitMCP.Addin.Compatibility;
using RevitMCP.Addin.Execution;
using RevitMCP.Bridge;
using RevitMCP.Contracts;

namespace RevitMCP.Addin.Capabilities;

internal sealed class RevitGetContextService : IRevitCapabilityService
{
    private readonly RevitExecutionDispatcher _dispatcher;
    private readonly BridgeInstanceMetadata _metadata;

    public RevitGetContextService(RevitExecutionDispatcher dispatcher, BridgeInstanceMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(metadata);
        _dispatcher = dispatcher;
        _metadata = metadata;
    }

    public async Task<GetContextResult> GetContextAsync(GetContextRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            return await _dispatcher.EnqueueAsync(Collect, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (RevitExecutionException)
        {
            throw new BridgeException(
                CapabilityErrorCodes.ExecutionFailed,
                "The Revit context could not be collected.");
        }
        catch (Exception)
        {
            throw new BridgeException(
                CapabilityErrorCodes.ExecutionFailed,
                "The Revit context could not be collected.");
        }
    }

    private GetContextResult Collect(UIApplication application)
    {
        var instance = new GetContextInstance
        {
            InstanceId = _metadata.InstanceId,
            RevitVersion = _metadata.RevitVersion,
            RevitBuild = _metadata.RevitBuild
        };

        var uiDocument = application.ActiveUIDocument;
        if (uiDocument is null)
        {
            return new GetContextResult
            {
                Instance = instance,
                Document = null,
                ActiveView = null,
                Selection = new GetContextSelection { Count = 0 }
            };
        }

        var document = uiDocument.Document;
        var view = uiDocument.ActiveView;
        return new GetContextResult
        {
            Instance = instance,
            Document = new GetContextDocument
            {
                Title = document.Title,
                Kind = document.IsFamilyDocument ? GetContextDocumentKind.Family : GetContextDocumentKind.Project,
                IsWorkshared = document.IsWorkshared,
                IsModelInCloud = document.IsModelInCloud,
                IsReadOnly = document.IsReadOnly,
                IsModified = document.IsModified
            },
            ActiveView = view is null
                ? null
                : new GetContextActiveView
                {
                    ElementId = RevitElementIds.Format(view.Id),
                    Name = view.Name,
                    ViewType = view.ViewType.ToString()
                },
            Selection = new GetContextSelection
            {
                Count = uiDocument.Selection.GetElementIds().Count
            }
        };
    }
}
