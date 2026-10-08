using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCP.Addin.Audit;
using RevitMCP.Addin.Identity;
using RevitMCP.Addin.Inspection;
using RevitMCP.Addin.Intents;
using RevitMCP.Contracts;

namespace RevitMCP.Addin.Capabilities;

internal static class RevitApplyParameterMutation
{
    internal const string TransactionName = "RevitMCP Apply Parameter Updates";

    public static ApplyMutationResult Execute(
        UIApplication application,
        OpenDocumentIdentityService identity,
        OpenDocumentParameterIdentityService parameterRefs,
        IntentEntry intent)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(parameterRefs);
        ArgumentNullException.ThrowIfNull(intent);

        if (!TryPrepare(application, identity, parameterRefs, intent, out var prepared))
        {
            return new ApplyMutationResult(ApplyMutationKind.Stale, AuditTransactionStatus.None);
        }

        return Commit(prepared);
    }

    private static bool TryPrepare(
        UIApplication application,
        OpenDocumentIdentityService identity,
        OpenDocumentParameterIdentityService parameterRefs,
        IntentEntry intent,
        out PreparedBatch prepared)
    {
        prepared = default;
        try
        {
            if (!TryActiveDocument(application, identity, intent.DocumentId, out var document))
            {
                return false;
            }

            var ordered = intent.Items.OrderBy(item => item.RequestPosition).ToArray();
            var parameters = new Parameter[ordered.Length];
            var internalQuantities = new double?[ordered.Length];
            for (var index = 0; index < ordered.Length; index++)
            {
                if (!TryResolveWritable(document, parameterRefs, ordered[index], out var parameter, out var internalQuantity))
                {
                    return false;
                }

                parameters[index] = parameter;
                internalQuantities[index] = internalQuantity;
            }

            prepared = new PreparedBatch(document, parameterRefs, ordered, parameters, internalQuantities);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static ApplyMutationResult Commit(PreparedBatch prepared)
    {
        Transaction? transaction = null;
        try
        {
            transaction = new Transaction(prepared.Document, TransactionName);

            TransactionStatus started;
            try
            {
                started = transaction.Start();
            }
            catch (Exception)
            {
                return Publish(ref transaction, ApplyTransactionCheckpoint.Start, returned: null, prepared);
            }

            if (started != TransactionStatus.Started)
            {
                return Publish(ref transaction, ApplyTransactionCheckpoint.Start, started, prepared);
            }

            try
            {
                // Start() resets failure handling. Install it only after Started, before any Set or rollback.
                var options = transaction.GetFailureHandlingOptions();
                options.SetFailuresPreprocessor(new ApplyRollbackFailuresPreprocessor());
                options.SetClearAfterRollback(true);
                transaction.SetFailureHandlingOptions(options);
            }
            catch (Exception)
            {
                return Rollback(ref transaction);
            }

            try
            {
                for (var index = 0; index < prepared.Items.Length; index++)
                {
                    if (!TrySet(prepared.Parameters[index], prepared.Items[index], prepared.InternalQuantities[index]))
                    {
                        return Rollback(ref transaction);
                    }
                }
            }
            catch (Exception)
            {
                return Rollback(ref transaction);
            }

            TransactionStatus committed;
            try
            {
                committed = transaction.Commit();
            }
            catch (Exception)
            {
                return Publish(ref transaction, ApplyTransactionCheckpoint.Commit, returned: null, prepared);
            }

            return Publish(ref transaction, ApplyTransactionCheckpoint.Commit, committed, prepared);
        }
        finally
        {
            SafeRelease(transaction);
        }
    }

    private static ApplyMutationResult Rollback(ref Transaction? transaction)
    {
        TransactionStatus? returned = null;
        try
        {
            returned = transaction!.RollBack();
        }
        catch (Exception)
        {
            returned = null;
        }

        return Publish(ref transaction, ApplyTransactionCheckpoint.Rollback, returned, prepared: default);
    }

    private static ApplyMutationResult Publish(
        ref Transaction? transaction,
        ApplyTransactionCheckpoint checkpoint,
        TransactionStatus? returned,
        PreparedBatch prepared)
    {
        var getStatus = ApplyObservedTransactionStatus.Unreadable;
        if (transaction is not null && TryReadStatus(transaction, out var status))
        {
            getStatus = MapStatus(status);
        }

        var returnedStatus = returned is TransactionStatus returnedValue
            ? MapStatus(returnedValue)
            : (ApplyObservedTransactionStatus?)null;
        var classification = ApplyTransactionStatusClassifier.Observe(
            checkpoint,
            getStatus,
            returnedStatus);

        if (classification.Result.Kind == ApplyMutationKind.Pending)
        {
            transaction = null;
            return classification.Result;
        }

        if (!classification.RequiresVerification)
        {
            return classification.Result;
        }

        return Verify(prepared)
            ? new ApplyMutationResult(ApplyMutationKind.Applied, AuditTransactionStatus.Committed)
            : new ApplyMutationResult(ApplyMutationKind.CommittedUnverified, AuditTransactionStatus.Committed);
    }

    private static ApplyObservedTransactionStatus MapStatus(TransactionStatus status)
    {
        return status switch
        {
            TransactionStatus.Pending => ApplyObservedTransactionStatus.Pending,
            TransactionStatus.RolledBack => ApplyObservedTransactionStatus.RolledBack,
            TransactionStatus.Committed => ApplyObservedTransactionStatus.Committed,
            _ => ApplyObservedTransactionStatus.Other
        };
    }

    private static bool TryReadStatus(Transaction transaction, out TransactionStatus status)
    {
        try
        {
            status = transaction.GetStatus();
            return true;
        }
        catch (Exception)
        {
            status = default;
            return false;
        }
    }

    private static void SafeRelease(Transaction? transaction)
    {
        if (transaction is null)
        {
            return;
        }

        try
        {
            if (transaction.GetStatus() == TransactionStatus.Pending)
            {
                return;
            }
        }
        catch (Exception)
        {
            return;
        }

        try
        {
            transaction.Dispose();
        }
        catch (Exception)
        {
            // Cleanup must not replace a result that was already classified.
        }
    }

    private static bool Verify(PreparedBatch prepared)
    {
        try
        {
            for (var index = 0; index < prepared.Items.Length; index++)
            {
                var item = prepared.Items[index];
                var parameter = FreshParameter(prepared.Document, prepared.ParameterRefs, item);
                if (parameter is null || !parameter.HasValue || !MatchesProposed(parameter, item, prepared.InternalQuantities[index]))
                {
                    return false;
                }
            }

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool TryActiveDocument(
        UIApplication application,
        OpenDocumentIdentityService identity,
        string documentId,
        out Document document)
    {
        document = null!;
        var uiDocument = application.ActiveUIDocument;
        if (uiDocument is null)
        {
            return false;
        }

        document = uiDocument.Document;
        if (document.IsFamilyDocument || document.IsReadOnly || !identity.TryGet(document, out var activeId))
        {
            return false;
        }

        return string.Equals(activeId, documentId, StringComparison.Ordinal);
    }

    private static bool TryResolveWritable(
        Document document,
        OpenDocumentParameterIdentityService parameterRefs,
        IntentItemEntry item,
        out Parameter parameter,
        out double? internalQuantity)
    {
        parameter = null!;
        internalQuantity = null;
        if (!string.Equals(item.Source, "instance", StringComparison.Ordinal)
            || !string.Equals(item.Status, "ok", StringComparison.Ordinal)
            || !parameterRefs.TryResolve(document, item.ParameterRef, out var binding)
            || binding.Source != GetElementParameterSource.Instance)
        {
            return false;
        }

        var element = document.GetElement(item.ElementRef);
        if (element is null || element is ElementType)
        {
            return false;
        }

        Parameter? match = null;
        foreach (var candidate in element.GetOrderedParameters())
        {
            if (RevitParameterIdentity.Classify(candidate, document) == new ClassifiedParameterIdentity(
                    item.IdentityKind,
                    item.ParameterTypeId,
                    item.SharedGuid,
                    item.StableKey))
            {
                match = candidate;
                break;
            }
        }

        if (match is null
            || !PreviewParameterWriteEligibility.IsWritable(match.IsReadOnly, match.IsShared, match.UserModifiable)
            || !KindMatches(match, item.Proposed)
            || !NamesMatch(element, match, item)
            || !DataTypeMatches(match, item)
            || !TryReadCurrent(match, item, out var hasValue, out var current, out internalQuantity))
        {
            return false;
        }

        if (!ApplyBeforeStateComparer.Matches(item, hasValue, current))
        {
            return false;
        }

        parameter = match;
        return true;
    }

    private static Parameter? FreshParameter(
        Document document,
        OpenDocumentParameterIdentityService parameterRefs,
        IntentItemEntry item)
    {
        if (document.IsFamilyDocument || document.IsReadOnly)
        {
            return null;
        }

        if (!parameterRefs.TryResolve(document, item.ParameterRef, out var binding)
            || binding.Source != GetElementParameterSource.Instance)
        {
            return null;
        }

        var element = document.GetElement(item.ElementRef);
        if (element is null || element is ElementType)
        {
            return null;
        }

        foreach (var candidate in element.GetOrderedParameters())
        {
            if (RevitParameterIdentity.Classify(candidate, document) == new ClassifiedParameterIdentity(
                    item.IdentityKind,
                    item.ParameterTypeId,
                    item.SharedGuid,
                    item.StableKey))
            {
                return candidate;
            }
        }

        return null;
    }

    private static bool KindMatches(Parameter parameter, IntentTypedValue proposed)
    {
        switch (proposed)
        {
            case IntentTypedValue.StringValue:
                return parameter.StorageType == StorageType.String;
            case IntentTypedValue.IntegerValue:
                var integerSpec = parameter.Definition?.GetDataType();
                return parameter.StorageType == StorageType.Integer
                    && integerSpec is not null
                    && integerSpec.Equals(SpecTypeId.Int.Integer);
            case IntentTypedValue.QuantityValue:
                var quantitySpec = parameter.Definition?.GetDataType();
                return parameter.StorageType == StorageType.Double
                    && quantitySpec is not null
                    && !quantitySpec.Empty()
                    && UnitUtils.IsMeasurableSpec(quantitySpec);
            default:
                return false;
        }
    }

    private static bool NamesMatch(Element element, Parameter parameter, IntentItemEntry item)
    {
        var elementName = ParameterValueTextBounder.Bound(element.Name ?? "");
        var categoryName = ParameterValueTextBounder.Bound(element.Category?.Name ?? "");
        var parameterName = ParameterValueTextBounder.Bound(parameter.Definition?.Name ?? "");
        return string.Equals(elementName.Text ?? "", item.ElementName, StringComparison.Ordinal)
            && elementName.Truncated == item.ElementNameTruncated
            && string.Equals(categoryName.Text ?? "", item.CategoryName, StringComparison.Ordinal)
            && categoryName.Truncated == item.CategoryNameTruncated
            && string.Equals(parameterName.Text ?? "", item.ParameterName, StringComparison.Ordinal)
            && parameterName.Truncated == item.ParameterNameTruncated;
    }

    private static bool DataTypeMatches(Parameter parameter, IntentItemEntry item)
    {
        var dataType = RevitParameterIdentity.ClassifyDataType(parameter);
        return dataType.Kind == item.DataTypeKind
            && string.Equals(dataType.ForgeTypeId, item.ForgeTypeId, StringComparison.Ordinal);
    }

    private static bool TryReadCurrent(
        Parameter parameter,
        IntentItemEntry item,
        out bool hasValue,
        out IntentTypedValue? current,
        out double? internalQuantity)
    {
        hasValue = parameter.HasValue;
        current = null;
        internalQuantity = null;
        switch (item.Proposed)
        {
            case IntentTypedValue.StringValue:
                if (!hasValue)
                {
                    return true;
                }

                var text = parameter.AsString();
                if (text is null)
                {
                    return false;
                }

                current = new IntentTypedValue.StringValue(text);
                return true;
            case IntentTypedValue.IntegerValue:
                if (!hasValue)
                {
                    return true;
                }

                current = new IntentTypedValue.IntegerValue(parameter.AsInteger());
                return true;
            case IntentTypedValue.QuantityValue quantity:
                if (!TryResolveUnit(parameter, quantity.UnitTypeId, out var unit))
                {
                    return false;
                }

                internalQuantity = UnitUtils.ConvertToInternalUnits(quantity.Value, unit);
                if (!hasValue)
                {
                    return true;
                }

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

                current = new IntentTypedValue.QuantityValue(converted, quantity.UnitTypeId);
                return true;
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

    private static bool TrySet(Parameter parameter, IntentItemEntry item, double? internalQuantity)
    {
        return item.Proposed switch
        {
            IntentTypedValue.StringValue text => parameter.Set(text.Value),
            IntentTypedValue.IntegerValue integer => parameter.Set(integer.Value),
            IntentTypedValue.QuantityValue when internalQuantity is double value => parameter.Set(value),
            _ => false
        };
    }

    private static bool MatchesProposed(Parameter parameter, IntentItemEntry item, double? internalQuantity)
    {
        return item.Proposed switch
        {
            IntentTypedValue.StringValue text =>
                string.Equals(parameter.AsString(), text.Value, StringComparison.Ordinal),
            IntentTypedValue.IntegerValue integer => parameter.AsInteger() == integer.Value,
            IntentTypedValue.QuantityValue when internalQuantity is double value => parameter.AsDouble() == value,
            _ => false
        };
    }

    private readonly record struct PreparedBatch(
        Document Document,
        OpenDocumentParameterIdentityService ParameterRefs,
        IntentItemEntry[] Items,
        Parameter[] Parameters,
        double?[] InternalQuantities);

    private sealed class ApplyRollbackFailuresPreprocessor : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            var failures = failuresAccessor.GetFailureMessages();
            if (failures is null || failures.Count == 0)
            {
                return FailureProcessingResult.Continue;
            }

            return FailureProcessingResult.ProceedWithRollBack;
        }
    }
}
