using Autodesk.Revit.UI;
using BimIt.Revit.Addin.Apply;
using BimIt.Revit.Addin.Approval;
using BimIt.Revit.Addin.Audit;
using BimIt.Revit.Addin.Execution;
using BimIt.Revit.Addin.Identity;
using BimIt.Revit.Addin.Intents;
using BimIt.Revit.Bridge;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Addin.Capabilities;

internal sealed class RevitApplyMepDevicePlacementsService : IRevitApplyMepDevicePlacementsService
{
    private readonly RevitExecutionDispatcher _dispatcher;
    private readonly BridgeInstanceMetadata _metadata;
    private readonly OpenDocumentIdentityService _identity;
    private readonly EphemeralWriteIntentStore _intentStore;
    private readonly RevitLocalApprovalProviderStateMachine _approval;
    private readonly ControlledApplyAttemptStore _attempts;
    private readonly ControlledWriteAuditWriter _audit;

    public RevitApplyMepDevicePlacementsService(
        RevitExecutionDispatcher dispatcher,
        BridgeInstanceMetadata metadata,
        OpenDocumentIdentityService identity,
        EphemeralWriteIntentStore intentStore,
        RevitLocalApprovalProviderStateMachine approval,
        ControlledApplyAttemptStore attempts,
        ControlledWriteAuditWriter audit)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(intentStore);
        ArgumentNullException.ThrowIfNull(approval);
        ArgumentNullException.ThrowIfNull(attempts);
        ArgumentNullException.ThrowIfNull(audit);
        _dispatcher = dispatcher;
        _metadata = metadata;
        _identity = identity;
        _intentStore = intentStore;
        _approval = approval;
        _attempts = attempts;
        _audit = audit;
    }

    public async Task<ApplyMepDevicePlacementsResult> ApplyMepDevicePlacementsAsync(
        ApplyMepDevicePlacementsRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ApplyMepDevicePlacementsRequests.Validate(request);
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
            throw Failed();
        }
        catch (Exception)
        {
            throw Failed();
        }
    }

    private ApplyMepDevicePlacementsResult Execute(UIApplication application, string intentRef)
    {
        return ApplyMepDevicePlacementOrchestrator.Execute(new MepApplyOperation
        {
            IntentRef = intentRef,
            InstanceId = _metadata.InstanceId,
            RevitVersion = _metadata.RevitVersion,
            RevitBuild = _metadata.RevitBuild,
            AddinVersion = _metadata.AddinVersion,
            Intents = _intentStore,
            Attempts = _attempts,
            Audit = _audit,
            Consume = intent => _approval.TryConsumeApproved(intent, ControlledWriteIntentKind.MepDevicePlacements),
            ResolveDocument = () => ResolveDocument(application),
            Mutate = intent => RevitMepDevicePlacementMutation.Execute(application, _identity, intent)
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
        if (document.IsFamilyDocument || document.IsReadOnly || !_identity.TryGet(document, out var documentId))
        {
            return new ApplyDocumentGate(false, null);
        }

        return new ApplyDocumentGate(true, documentId);
    }

    private static BridgeException Failed()
    {
        return new BridgeException(
            CapabilityErrorCodes.ExecutionFailed,
            "The MEP device placement apply could not be executed.");
    }
}
