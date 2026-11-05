using System.Globalization;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Addin.Inspection;

internal readonly record struct ClassifiedParameterIdentity(
    DescribeParameterIdentityKind Kind,
    string? ParameterTypeId,
    string? SharedGuid,
    string StableKey);

/// <summary>
/// What a non-built-in definition's element contributes to its identity:
/// the shared GUID when it is a <c>SharedParameterElement</c>, and the local stable key.
/// </summary>
internal readonly record struct DefinitionElementFacts(Guid? SharedGuid, string LocalStableKey);

internal static class ParameterIdentityClassifier
{
    public static ClassifiedParameterIdentity Classify(
        bool isBuiltIn,
        string? builtInTypeId,
        bool isShared,
        Guid sharedGuid,
        string localStableKey)
    {
        if (isBuiltIn && !string.IsNullOrEmpty(builtInTypeId))
        {
            return new ClassifiedParameterIdentity(
                DescribeParameterIdentityKind.BuiltIn,
                builtInTypeId,
                null,
                builtInTypeId);
        }

        if (isShared && sharedGuid != Guid.Empty)
        {
            var guid = sharedGuid.ToString("D").ToLowerInvariant();
            return new ClassifiedParameterIdentity(
                DescribeParameterIdentityKind.Shared,
                null,
                guid,
                guid);
        }

        ArgumentException.ThrowIfNullOrEmpty(localStableKey);
        return new ClassifiedParameterIdentity(
            DescribeParameterIdentityKind.Local,
            null,
            null,
            localStableKey);
    }

    /// <summary>
    /// The definition-based rule of CAP-0015: built-in when the type id passed the built-in gate,
    /// and then the definition element is never looked up; otherwise
    /// <paramref name="resolveDefinitionElement"/> is invoked exactly once, and the identity is
    /// shared when the element is a <c>SharedParameterElement</c> (a non-null GUID), else local.
    /// </summary>
    public static ClassifiedParameterIdentity ClassifyDefinition(
        string? builtInTypeId,
        Func<DefinitionElementFacts> resolveDefinitionElement)
    {
        ArgumentNullException.ThrowIfNull(resolveDefinitionElement);
        if (!string.IsNullOrEmpty(builtInTypeId))
        {
            return Classify(true, builtInTypeId, false, Guid.Empty, builtInTypeId);
        }

        var element = resolveDefinitionElement();
        return Classify(
            false,
            null,
            element.SharedGuid.HasValue,
            element.SharedGuid ?? Guid.Empty,
            element.LocalStableKey);
    }

    /// <summary>
    /// Null-safe built-in gate. <paramref name="isBuiltInParameter"/> is invoked only for a
    /// non-null, non-empty type id, so a custom definition without a type id never reaches it.
    /// </summary>
    public static bool IsBuiltIn(string? parameterTypeId, Func<bool> isBuiltInParameter)
    {
        ArgumentNullException.ThrowIfNull(isBuiltInParameter);
        return !string.IsNullOrEmpty(parameterTypeId) && isBuiltInParameter();
    }

    /// <summary>
    /// The document-scoped local stable key: the parameter element's <c>UniqueId</c> when present,
    /// otherwise <c>local:</c> and the element id.
    /// </summary>
    public static string LocalStableKey(string? parameterElementUniqueId, long fallbackElementId)
    {
        return string.IsNullOrEmpty(parameterElementUniqueId)
            ? "local:" + fallbackElementId.ToString(CultureInfo.InvariantCulture)
            : parameterElementUniqueId;
    }

    public static DescribeParameterIdentity ToContract(ClassifiedParameterIdentity identity)
    {
        return new DescribeParameterIdentity
        {
            Kind = identity.Kind,
            ParameterTypeId = identity.ParameterTypeId,
            Guid = identity.SharedGuid
        };
    }
}
