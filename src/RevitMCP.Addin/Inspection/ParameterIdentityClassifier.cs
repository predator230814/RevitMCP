using RevitMCP.Contracts;

namespace RevitMCP.Addin.Inspection;

internal readonly record struct ClassifiedParameterIdentity(
    DescribeParameterIdentityKind Kind,
    string? ParameterTypeId,
    string? SharedGuid,
    string StableKey);

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
