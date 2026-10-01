using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCP.Addin.Execution;
using RevitMCP.Addin.Identity;
using RevitMCP.Addin.Inspection;
using RevitMCP.Addin.Intents;
using RevitMCP.Bridge;
using RevitMCP.Contracts;

namespace RevitMCP.Addin.Capabilities;

internal sealed class RevitPreviewParameterUpdatesService : IRevitPreviewParameterUpdatesService
{
    private readonly RevitExecutionDispatcher _dispatcher;
    private readonly BridgeInstanceMetadata _metadata;
    private readonly OpenDocumentIdentityService _identity;
    private readonly OpenDocumentParameterIdentityService _parameterRefs;
    private readonly EphemeralWriteIntentStore _intentStore;

    public RevitPreviewParameterUpdatesService(
        RevitExecutionDispatcher dispatcher,
        BridgeInstanceMetadata metadata,
        OpenDocumentIdentityService identity,
        OpenDocumentParameterIdentityService parameterRefs,
        EphemeralWriteIntentStore intentStore)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(parameterRefs);
        ArgumentNullException.ThrowIfNull(intentStore);
        _dispatcher = dispatcher;
        _metadata = metadata;
        _identity = identity;
        _parameterRefs = parameterRefs;
        _intentStore = intentStore;
    }

    public async Task<PreviewParameterUpdatesResult> PreviewParameterUpdatesAsync(
        PreviewParameterUpdatesRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        PreviewParameterUpdatesRequestValidator.Validate(request);

        try
        {
            return await _dispatcher.EnqueueAsync(application => Execute(application, request), cancellationToken)
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
                "The Revit parameter update preview could not be executed.");
        }
        catch (Exception)
        {
            throw new BridgeException(
                CapabilityErrorCodes.ExecutionFailed,
                "The Revit parameter update preview could not be executed.");
        }
    }

    private PreviewParameterUpdatesResult Execute(UIApplication application, PreviewParameterUpdatesRequest request)
    {
        var uiDocument = application.ActiveUIDocument;
        if (uiDocument is null)
        {
            throw new BridgeException(
                CapabilityErrorCodes.NoActiveDocument,
                "No active Revit document is available.");
        }

        var document = uiDocument.Document;
        var documentId = _identity.GetId(document);
        if (!string.Equals(request.DocumentId, documentId, StringComparison.Ordinal))
        {
            throw new BridgeException(
                CapabilityErrorCodes.DocumentContextChanged,
                "The supplied document_id does not match the active document.");
        }

        if (document.IsFamilyDocument)
        {
            throw new BridgeException(
                CapabilityErrorCodes.UnsupportedDocumentKind,
                "The active document is not a project document.");
        }

        if (document.IsReadOnly)
        {
            throw new BridgeException(
                CapabilityErrorCodes.DocumentNotWritable,
                "The active document is read-only.");
        }

        var candidates = new List<PreviewParameterUpdateCandidate>(request.Updates.Count);
        foreach (var update in request.Updates)
        {
            candidates.Add(EvaluateUpdate(document, update));
        }

        return PreviewParameterUpdateResults.Complete(
            new DescribeParametersContext
            {
                InstanceId = _metadata.InstanceId,
                DocumentId = documentId
            },
            candidates,
            _intentStore.TryCreate);
    }

    private PreviewParameterUpdateCandidate EvaluateUpdate(Document document, PreviewParameterUpdate update)
    {
        if (!_parameterRefs.TryResolve(document, update.ParameterRef, out var binding))
        {
            return Failure(update, PreviewParameterUpdateStatus.ParameterRefNotFound);
        }

        var element = document.GetElement(update.ElementRef);
        if (element is null || element is ElementType)
        {
            return Failure(update, PreviewParameterUpdateStatus.ElementNotFound);
        }

        if (binding.Source != GetElementParameterSource.Instance)
        {
            return Failure(update, PreviewParameterUpdateStatus.UnsupportedParameterSource);
        }

        Parameter? match = null;
        ClassifiedParameterIdentity identity = default;
        foreach (var parameter in element.GetOrderedParameters())
        {
            var classified = RevitParameterIdentity.Classify(parameter, document);
            if (classified == binding.Identity)
            {
                match = parameter;
                identity = classified;
                break;
            }
        }

        if (match is null)
        {
            return Failure(update, PreviewParameterUpdateStatus.ParameterNotPresent);
        }

        if (!PreviewParameterWriteEligibility.IsWritable(match.IsReadOnly, match.IsShared, match.UserModifiable))
        {
            return Failure(update, PreviewParameterUpdateStatus.ParameterNotWritable);
        }

        if (!ProposedKindMatches(match, update.Value))
        {
            return Failure(update, PreviewParameterUpdateStatus.ValueTypeMismatch);
        }

        ForgeTypeId? unit = null;
        if (update.Value is PreviewParameterQuantityValue quantity && !TryResolveUnit(match, quantity.UnitTypeId, out unit))
        {
            return Failure(update, PreviewParameterUpdateStatus.InvalidUnit);
        }

        if (!TryReadBefore(match, update.Value, unit, out var hasValue, out var before))
        {
            return Failure(update, PreviewParameterUpdateStatus.UnsupportedValue);
        }

        if (hasValue && before is not null && ValuesEqual(before, update.Value))
        {
            return Accepted(update, element, match, identity, PreviewParameterUpdateStatus.NoChange, hasValue, before);
        }

        return Accepted(update, element, match, identity, PreviewParameterUpdateStatus.Ok, hasValue, before);
    }

    private static bool ProposedKindMatches(Parameter parameter, PreviewParameterValue proposed)
    {
        switch (proposed)
        {
            case PreviewParameterStringValue:
                return parameter.StorageType == StorageType.String;
            case PreviewParameterIntegerValue:
                var integerSpec = parameter.Definition?.GetDataType();
                return parameter.StorageType == StorageType.Integer
                    && integerSpec is not null
                    && integerSpec.Equals(SpecTypeId.Int.Integer);
            case PreviewParameterQuantityValue:
                var quantitySpec = parameter.Definition?.GetDataType();
                return parameter.StorageType == StorageType.Double
                    && quantitySpec is not null
                    && !quantitySpec.Empty()
                    && UnitUtils.IsMeasurableSpec(quantitySpec);
            default:
                return false;
        }
    }

    private static bool TryResolveUnit(Parameter parameter, string unitTypeId, out ForgeTypeId unit)
    {
        unit = null!;
        ForgeTypeId candidate;
        try
        {
            candidate = new ForgeTypeId(unitTypeId);
        }
        catch (ArgumentException)
        {
            return false;
        }

        if (candidate.Empty() || !UnitUtils.IsUnit(candidate))
        {
            return false;
        }

        var spec = parameter.Definition?.GetDataType();
        if (spec is null || spec.Empty() || !UnitUtils.IsValidUnit(spec, candidate))
        {
            return false;
        }

        unit = candidate;
        return true;
    }

    private static bool TryReadBefore(
        Parameter parameter,
        PreviewParameterValue proposed,
        ForgeTypeId? unit,
        out bool hasValue,
        out PreviewParameterValue? before)
    {
        hasValue = parameter.HasValue;
        before = null;
        if (!hasValue)
        {
            return true;
        }

        switch (proposed)
        {
            case PreviewParameterStringValue:
                var text = parameter.AsString();
                if (text is null || text.Length > ParameterValueTextBounder.MaxLength)
                {
                    return false;
                }

                before = new PreviewParameterStringValue { Value = text };
                return true;
            case PreviewParameterIntegerValue:
                before = new PreviewParameterIntegerValue { Value = parameter.AsInteger() };
                return true;
            case PreviewParameterQuantityValue quantity when unit is not null:
                double converted;
                try
                {
                    converted = UnitUtils.ConvertFromInternalUnits(parameter.AsDouble(), unit);
                }
                catch (ArgumentException)
                {
                    return false;
                }

                if (!double.IsFinite(converted))
                {
                    return false;
                }

                before = new PreviewParameterQuantityValue
                {
                    Value = converted,
                    UnitTypeId = quantity.UnitTypeId
                };
                return true;
            default:
                return false;
        }
    }

    private static bool ValuesEqual(PreviewParameterValue before, PreviewParameterValue proposed)
    {
        return (before, proposed) switch
        {
            (PreviewParameterStringValue left, PreviewParameterStringValue right) =>
                string.Equals(left.Value, right.Value, StringComparison.Ordinal),
            (PreviewParameterIntegerValue left, PreviewParameterIntegerValue right) => left.Value == right.Value,
            (PreviewParameterQuantityValue left, PreviewParameterQuantityValue right) => left.Value == right.Value,
            _ => false
        };
    }

    private static PreviewParameterUpdateCandidate Accepted(
        PreviewParameterUpdate update,
        Element element,
        Parameter parameter,
        ClassifiedParameterIdentity identity,
        PreviewParameterUpdateStatus status,
        bool hasValue,
        PreviewParameterValue? before)
    {
        var elementName = BoundName(element.Name);
        var categoryName = BoundName(element.Category?.Name ?? "");
        var parameterName = BoundName(parameter.Definition?.Name ?? "");
        return new PreviewParameterUpdateCandidate
        {
            ElementRef = update.ElementRef,
            ParameterRef = update.ParameterRef,
            Status = status,
            ElementName = elementName.Text,
            ElementNameTruncated = elementName.Truncated,
            CategoryName = categoryName.Text,
            CategoryNameTruncated = categoryName.Truncated,
            ParameterName = parameterName.Text,
            ParameterNameTruncated = parameterName.Truncated,
            DataType = RevitParameterIdentity.ClassifyDataType(parameter),
            BeforeHasValue = hasValue,
            BeforeValue = before,
            Proposed = update.Value,
            IdentityKind = identity.Kind,
            ParameterTypeId = identity.ParameterTypeId,
            SharedGuid = identity.SharedGuid,
            StableKey = identity.StableKey
        };
    }

    private static (string Text, bool Truncated) BoundName(string? value)
    {
        var bounded = ParameterValueTextBounder.Bound(value ?? "");
        return (bounded.Text ?? "", bounded.Truncated);
    }

    private static PreviewParameterUpdateCandidate Failure(PreviewParameterUpdate update, PreviewParameterUpdateStatus status)
    {
        return new PreviewParameterUpdateCandidate
        {
            ElementRef = update.ElementRef,
            ParameterRef = update.ParameterRef,
            Status = status
        };
    }
}
