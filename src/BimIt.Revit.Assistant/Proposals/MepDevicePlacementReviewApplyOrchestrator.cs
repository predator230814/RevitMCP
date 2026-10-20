using System.Globalization;
using System.Text.Json;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Assistant;

/// <summary>
/// Local review and apply for the current preview receipt. The model cannot invoke it, and it does not read the selection.
/// </summary>
internal sealed class MepDevicePlacementReviewApplyOrchestrator
{
    public const string ReviewTitle = "Revit review";

    public const string ApplyTitle = "Placement apply";

    private readonly IMepDevicePlacementReviewClient _review;
    private readonly IMepDevicePlacementApplyClient _apply;
    private readonly MepDevicePlacementProposalStore _store;
    private readonly string _instanceId;
    private readonly Func<DateTimeOffset> _utcNow;

    public MepDevicePlacementReviewApplyOrchestrator(
        IMepDevicePlacementReviewClient review,
        IMepDevicePlacementApplyClient apply,
        MepDevicePlacementProposalStore store,
        string instanceId,
        Func<DateTimeOffset>? utcNow = null)
    {
        _review = review ?? throw new ArgumentNullException(nameof(review));
        _apply = apply ?? throw new ArgumentNullException(nameof(apply));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _instanceId = instanceId ?? throw new ArgumentNullException(nameof(instanceId));
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task<MepDevicePlacementPreviewOutcome> ReviewAsync(CancellationToken cancellationToken)
    {
        if (!TryCurrentIntent("There is no placement preview to review.", out var intentRef, out var failure))
        {
            return Fail(ReviewTitle, failure);
        }

        AssistantToolCall call;
        try
        {
            call = await _review.ReviewAsync(_instanceId, intentRef, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return Fail(ReviewTitle, "The Revit review could not be opened.");
        }

        if (call.IsError || call.Structured is not JsonElement structured)
        {
            return Fail(ReviewTitle, "The Revit review could not be opened.");
        }

        RequestMepDevicePlacementReviewResult? result;
        try
        {
            result = structured.Deserialize<RequestMepDevicePlacementReviewResult>(ContractJson.Options);
        }
        catch (JsonException)
        {
            result = null;
        }

        if (result is null)
        {
            return Fail(ReviewTitle, "The Revit review could not be opened.");
        }

        return result.Status switch
        {
            RequestMepDevicePlacementReviewStatus.Started => Ok(ReviewTitle, "Revit review opened."),
            RequestMepDevicePlacementReviewStatus.AlreadyActive => Ok(ReviewTitle, "The same review is already open."),
            RequestMepDevicePlacementReviewStatus.Busy => Ok(ReviewTitle, "Another review is active."),
            RequestMepDevicePlacementReviewStatus.Terminal => Ok(ReviewTitle, "This intent already has a local terminal decision."),
            _ => Fail(ReviewTitle, "The Revit review could not be opened."),
        };
    }

    public async Task<MepDevicePlacementPreviewOutcome> ApplyAsync(CancellationToken cancellationToken)
    {
        if (_store.TryReuseTerminalApply(out var cached, out var cachedError))
        {
            return new MepDevicePlacementPreviewOutcome(cachedError, ApplyTitle, cached);
        }

        if (!TryCurrentIntent("There is no placement preview to apply.", out var intentRef, out var failure))
        {
            return Fail(ApplyTitle, failure);
        }

        var proposal = _store.Current;
        if (proposal is null)
        {
            return Fail(ApplyTitle, failure);
        }

        AssistantToolCall call;
        try
        {
            call = await _apply.ApplyAsync(_instanceId, intentRef, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return Fail(ApplyTitle, "The placement apply could not be completed.");
        }

        if (call.IsError || call.Structured is not JsonElement structured)
        {
            return Fail(ApplyTitle, "The placement apply could not be completed.");
        }

        ApplyMepDevicePlacementsResult? result;
        try
        {
            result = structured.Deserialize<ApplyMepDevicePlacementsResult>(ContractJson.Options);
        }
        catch (JsonException)
        {
            result = null;
        }

        if (result is null)
        {
            return Fail(ApplyTitle, "The placement apply could not be completed.");
        }

        return result.Status switch
        {
            ApplyMepDevicePlacementsStatus.Applied => AcceptApplied(proposal, intentRef, result),
            ApplyMepDevicePlacementsStatus.ApprovalRequired => Ok(ApplyTitle, "Revit-local approval is still required. No placements were created."),
            ApplyMepDevicePlacementsStatus.InProgress => Ok(ApplyTitle, "The placement apply is already in progress."),
            ApplyMepDevicePlacementsStatus.Unavailable => Fail(ApplyTitle, "The placement apply could not be completed."),
            ApplyMepDevicePlacementsStatus.Stale => Terminal(intentRef, "This placement preview is stale. No automatic retry will be attempted."),
            ApplyMepDevicePlacementsStatus.TransactionFailed => Terminal(intentRef, "The placement transaction failed. No automatic retry will be attempted."),
            ApplyMepDevicePlacementsStatus.CommittedUnverified => Terminal(intentRef, "A transaction committed, but Revit verification failed. Do not retry this placement."),
            ApplyMepDevicePlacementsStatus.Indeterminate => Terminal(intentRef, "The placement result is indeterminate. No automatic retry will be attempted."),
            ApplyMepDevicePlacementsStatus.AuditFailed => Terminal(intentRef, "The placement audit failed. No automatic retry will be attempted."),
            _ => Fail(ApplyTitle, "The placement apply could not be completed."),
        };
    }

    private MepDevicePlacementPreviewOutcome AcceptApplied(
        MepDevicePlacementProposal proposal,
        string intentRef,
        ApplyMepDevicePlacementsResult result)
    {
        var count = proposal.Placements.Count;
        if (result.CreatedCount != count
            || result.CreatedElementRefs is null
            || result.CreatedElementRefs.Count != count
            || result.CreatedElementRefs.Any(item => string.IsNullOrWhiteSpace(item)))
        {
            return Fail(ApplyTitle, "The placement apply could not be completed.");
        }

        var summary = "Applied "
            + count.ToString(CultureInfo.InvariantCulture)
            + " placements. Revit verification passed.";
        _store.RememberTerminalApply(intentRef, summary, false);
        return Ok(ApplyTitle, summary);
    }

    private MepDevicePlacementPreviewOutcome Terminal(string intentRef, string summary)
    {
        _store.RememberTerminalApply(intentRef, summary, true);
        return Fail(ApplyTitle, summary);
    }

    private bool TryCurrentIntent(string missingMessage, out string intentRef, out string failure)
    {
        intentRef = "";
        var proposal = _store.Current;
        var preview = _store.Preview;
        if (proposal is null
            || preview is null
            || !string.Equals(proposal.InstanceId, _instanceId, StringComparison.Ordinal))
        {
            failure = missingMessage;
            return false;
        }

        if (!_store.TryGetUnexpiredPreview(_utcNow(), out var receipt))
        {
            failure = "The placement preview has expired. Confirm project axes and preview again.";
            return false;
        }

        intentRef = receipt.IntentRef;
        failure = "";
        return true;
    }

    private static MepDevicePlacementPreviewOutcome Ok(string title, string body)
    {
        return new MepDevicePlacementPreviewOutcome(false, title, body);
    }

    private static MepDevicePlacementPreviewOutcome Fail(string title, string body)
    {
        return new MepDevicePlacementPreviewOutcome(true, title, body);
    }
}
