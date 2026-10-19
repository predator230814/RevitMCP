using System.Globalization;
using System.Text.Json;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Assistant;

internal readonly record struct MepDevicePlacementPreviewOutcome(bool IsError, string Title, string Body);

/// <summary>
/// Local project-axis confirmation and one MEP placement preview. The model cannot invoke it.
/// </summary>
internal sealed class MepDevicePlacementPreviewOrchestrator
{
    public const string Title = "Placement preview";

    private readonly IAssistantToolSession _session;
    private readonly IMepDevicePlacementPreviewClient _preview;
    private readonly MepDevicePlacementProposalStore _store;
    private readonly string _instanceId;
    private readonly Func<DateTimeOffset> _utcNow;

    public MepDevicePlacementPreviewOrchestrator(
        IAssistantToolSession session,
        IMepDevicePlacementPreviewClient preview,
        MepDevicePlacementProposalStore store,
        string instanceId,
        Func<DateTimeOffset>? utcNow = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _preview = preview ?? throw new ArgumentNullException(nameof(preview));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _instanceId = instanceId ?? throw new ArgumentNullException(nameof(instanceId));
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task<MepDevicePlacementPreviewOutcome> ConfirmAsync(CancellationToken cancellationToken)
    {
        var proposal = _store.Current;
        if (proposal is null)
        {
            return Fail("There is no placement proposal to preview.");
        }

        if (!proposal.HasTemplateBinding || !string.Equals(proposal.InstanceId, _instanceId, StringComparison.Ordinal))
        {
            return Fail("The proposal has no selected template. Create a new proposal.");
        }

        if (!await BindingStillMatchesAsync(proposal, cancellationToken).ConfigureAwait(false))
        {
            return Fail("The selected template no longer matches this proposal. Create a new proposal.");
        }

        if (_store.TryReusePreview(_utcNow(), out var existing))
        {
            return Ok(existing);
        }

        if (!_store.TryPromoteToReady(new TrustedProjectAxisCalibration()))
        {
            return Fail("The proposal has no selected template. Create a new proposal.");
        }

        if (!MepDevicePlacementLengthUnits.TryForgeTypeId(proposal.LengthUnit, out var lengthUnitId))
        {
            return Fail("The placement preview could not be created.");
        }

        var request = new PreviewMepDevicePlacementsRequest
        {
            DocumentId = proposal.DocumentId!,
            TemplateElementRef = proposal.TemplateElementRef!,
            LengthUnitId = lengthUnitId,
            Placements = proposal.Placements.Select(item => new PreviewMepDevicePlacement
            {
                OffsetX = item.OffsetX,
                OffsetY = item.OffsetY,
                VerticalOffset = item.VerticalOffset,
            }).ToArray(),
        };

        AssistantToolCall call;
        try
        {
            call = await _preview.PreviewAsync(request, proposal.InstanceId, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return Fail("The placement preview could not be created.");
        }

        if (call.IsError || call.Structured is not JsonElement structured)
        {
            return Fail("The placement preview could not be created.");
        }

        PreviewMepDevicePlacementsResult? result;
        try
        {
            result = structured.Deserialize<PreviewMepDevicePlacementsResult>(ContractJson.Options);
        }
        catch (JsonException)
        {
            result = null;
        }

        if (result is null)
        {
            return Fail("The placement preview could not be created.");
        }

        var summary = Describe(proposal, result);
        if (!string.IsNullOrWhiteSpace(result.IntentRef) && result.ExpiresAt is DateTimeOffset expiresAt)
        {
            _store.RememberPreview(result.IntentRef, result.IntentFingerprint, expiresAt, summary);
        }
        else
        {
            _store.ForgetPreview();
        }

        return Ok(summary);
    }

    private async Task<bool> BindingStillMatchesAsync(MepDevicePlacementProposal proposal, CancellationToken cancellationToken)
    {
        AssistantToolCall call;
        try
        {
            call = await _session.CallAsync(
                AssistantTools.GetSelection,
                new Dictionary<string, object?>
                {
                    ["instance_id"] = _instanceId,
                    ["limit"] = MepDevicePlacementSelectionProbe.Limit,
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return false;
        }

        if (call.IsError
            || !MepDevicePlacementSelectionProbe.TryRead(call.Structured, out var documentId, out var selectedCount, out var truncated, out var refs))
        {
            return false;
        }

        return MepDevicePlacementSelectionProbe.IsSameTemplate(proposal, documentId, selectedCount, truncated, refs);
    }

    private static string Describe(MepDevicePlacementProposal proposal, PreviewMepDevicePlacementsResult result)
    {
        var lines = new List<string>
        {
            "Project axes confirmed. Offsets are relative to the selected template. X and Y are Revit project axes. Z is vertical. This does not calibrate PDF or sheet coordinates.",
            "Placements: " + proposal.Placements.Count.ToString(CultureInfo.InvariantCulture),
            "Length unit: " + proposal.LengthUnit,
            "Preview ready: " + (result.Ready ? "yes" : "no"),
        };
        var template = DescribeTemplate(result.Template);
        if (template.Length > 0)
        {
            lines.Add(template);
        }

        if (result.Template.PlacementMode is PreviewMepDevicePlacementMode mode)
        {
            lines.Add("Placement mode: " + DescribeMode(mode));
        }

        if (result.Template.Status != PreviewMepDeviceTemplateStatus.Ok)
        {
            lines.Add("Template status: " + DescribeTemplateStatus(result.Template.Status));
        }

        for (var index = 0; index < result.Items.Count; index++)
        {
            var item = result.Items[index];
            lines.Add(
                (index + 1).ToString(CultureInfo.InvariantCulture)
                + ". "
                + DescribeItemStatus(item.Status)
                + " ("
                + Format(item.OffsetX)
                + ", "
                + Format(item.OffsetY)
                + ", "
                + Format(item.VerticalOffset)
                + ")");
        }

        return string.Join('\n', lines);
    }

    private static string DescribeTemplate(PreviewMepDeviceTemplate template)
    {
        var parts = new[] { template.FamilyName, template.TypeName, template.CategoryName, template.LevelName }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .ToArray();
        return parts.Length == 0 ? "" : "Template: " + string.Join(", ", parts);
    }

    private static string DescribeMode(PreviewMepDevicePlacementMode mode)
    {
        return mode switch
        {
            PreviewMepDevicePlacementMode.LevelBased => "Level-based",
            PreviewMepDevicePlacementMode.WallHosted => "Wall-hosted",
            PreviewMepDevicePlacementMode.WorkPlaneBasedLevel => "Level work plane",
            PreviewMepDevicePlacementMode.WorkPlaneBasedFace => "Face-based work plane",
            PreviewMepDevicePlacementMode.WorkPlaneBasedOther => "Other work plane",
            _ => "Unknown",
        };
    }

    private static string DescribeTemplateStatus(PreviewMepDeviceTemplateStatus status)
    {
        return status switch
        {
            PreviewMepDeviceTemplateStatus.NotFound => "not found",
            PreviewMepDeviceTemplateStatus.NotFamilyInstance => "not a family instance",
            PreviewMepDeviceTemplateStatus.UnsupportedCategory => "unsupported category",
            PreviewMepDeviceTemplateStatus.UnsupportedPlacement => "unsupported placement",
            PreviewMepDeviceTemplateStatus.UnsupportedHost => "unsupported host",
            PreviewMepDeviceTemplateStatus.UnsupportedHostGeometry => "unsupported host geometry",
            PreviewMepDeviceTemplateStatus.UnsupportedLocation => "unsupported location",
            PreviewMepDeviceTemplateStatus.UnsupportedLevel => "unsupported level",
            _ => "ok",
        };
    }

    private static string DescribeItemStatus(PreviewMepDevicePlacementItemStatus status)
    {
        return status switch
        {
            PreviewMepDevicePlacementItemStatus.OutOfHostBounds => "out of host bounds",
            PreviewMepDevicePlacementItemStatus.Occupied => "occupied",
            PreviewMepDevicePlacementItemStatus.UnsupportedPosition => "unsupported position",
            _ => "ok",
        };
    }

    private static string Format(double value)
    {
        return value.ToString("0.################", CultureInfo.InvariantCulture);
    }

    private static MepDevicePlacementPreviewOutcome Ok(string body)
    {
        return new MepDevicePlacementPreviewOutcome(false, Title, body);
    }

    private static MepDevicePlacementPreviewOutcome Fail(string body)
    {
        return new MepDevicePlacementPreviewOutcome(true, Title, body);
    }
}
