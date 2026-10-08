using Autodesk.Revit.UI;
using RevitMCP.Addin.Apply;
using RevitMCP.Addin.Approval;
using RevitMCP.Addin.Audit;
using RevitMCP.Addin.Execution;
using RevitMCP.Addin.Identity;
using RevitMCP.Addin.Intents;
using RevitMCP.Bridge;
using RevitMCP.Contracts;

namespace RevitMCP.Addin.Capabilities;

internal sealed class RevitApplyParameterUpdatesService : IRevitApplyParameterUpdatesService
{
    private readonly RevitExecutionDispatcher _dispatcher;
    private readonly BridgeInstanceMetadata _metadata;
    private readonly OpenDocumentIdentityService _identity;
    private readonly OpenDocumentParameterIdentityService _parameterRefs;
    private readonly EphemeralWriteIntentStore _intentStore;
    private readonly RevitLocalApprovalProviderStateMachine _approval;
    private readonly ControlledApplyAttemptStore _attempts;
    private readonly ControlledWriteAuditWriter _audit;

    public RevitApplyParameterUpdatesService(
        RevitExecutionDispatcher dispatcher,
        BridgeInstanceMetadata metadata,
        OpenDocumentIdentityService identity,
        OpenDocumentParameterIdentityService parameterRefs,
        EphemeralWriteIntentStore intentStore,
        RevitLocalApprovalProviderStateMachine approval,
        ControlledApplyAttemptStore attempts,
        ControlledWriteAuditWriter audit)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(parameterRefs);
        ArgumentNullException.ThrowIfNull(intentStore);
        ArgumentNullException.ThrowIfNull(approval);
        ArgumentNullException.ThrowIfNull(attempts);
        ArgumentNullException.ThrowIfNull(audit);
        _dispatcher = dispatcher;
        _metadata = metadata;
        _identity = identity;
        _parameterRefs = parameterRefs;
        _intentStore = intentStore;
        _approval = approval;
        _attempts = attempts;
        _audit = audit;
    }

    public async Task<ApplyParameterUpdatesResult> ApplyParameterUpdatesAsync(
        ApplyParameterUpdatesRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ApplyParameterUpdatesRequests.Validate(request);

        try
        {
            return await _dispatcher.EnqueueAsync(application => Execute(application, request.IntentRef), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (BridgeException)
        {
            throw;
        }
        catch (RevitExecutionException)
        {
            throw new BridgeException(
                CapabilityErrorCodes.ExecutionFailed,
                "The Revit parameter update apply could not be executed.");
        }
        catch (Exception)
        {
            throw new BridgeException(
                CapabilityErrorCodes.ExecutionFailed,
                "The Revit parameter update apply could not be executed.");
        }
    }

    private ApplyParameterUpdatesResult Execute(UIApplication application, string intentRef)
    {
        return ApplyParameterUpdateOrchestrator.Execute(new ApplyOperation
        {
            IntentRef = intentRef,
            InstanceId = _metadata.InstanceId,
            RevitVersion = _metadata.RevitVersion,
            RevitBuild = _metadata.RevitBuild,
            AddinVersion = _metadata.AddinVersion,
            Intents = _intentStore,
            Attempts = _attempts,
            Audit = _audit,
            Consume = intent => _approval.TryConsumeApproved(intent),
            ResolveDocument = () => ResolveDocument(application),
            Mutate = intent => RevitApplyParameterMutation.Execute(application, _identity, _parameterRefs, intent)
        });
    }

    private ApplyDocumentGate ResolveDocument(UIApplication application)
    {
        var uiDocument = application.ActiveUIDocument;
        if (uiDocument is null)
        {
            return new ApplyDocumentGate(false, null);
        }

        var document = uiDocument.Document;
        if (document.IsFamilyDocument
            || document.IsReadOnly
            || !_identity.TryGet(document, out var documentId))
        {
            return new ApplyDocumentGate(false, null);
        }

        return new ApplyDocumentGate(true, documentId);
    }
}
