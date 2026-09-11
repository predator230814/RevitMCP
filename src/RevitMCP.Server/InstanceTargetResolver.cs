using RevitMCP.Contracts;

namespace RevitMCP.Server;

internal static class InstanceTargetResolver
{
    public static TargetResolution Resolve(IReadOnlyList<DiscoveredInstance> discovered, string? instanceId)
    {
        return ResolveForGetContext(discovered, instanceId);
    }

    public static TargetResolution ResolveForGetContext(IReadOnlyList<DiscoveredInstance> discovered, string? instanceId)
    {
        return Resolve(discovered, instanceId, IsGetContextEligible);
    }

    public static TargetResolution ResolveForQueryElements(IReadOnlyList<DiscoveredInstance> discovered, string? instanceId)
    {
        return Resolve(discovered, instanceId, IsQueryElementsEligible);
    }

    public static bool IsGetContextEligible(DiscoveredInstance instance)
    {
        return IsEligible(instance, BridgeProtocol.SupportsGetContext);
    }

    public static bool IsQueryElementsEligible(DiscoveredInstance instance)
    {
        return IsEligible(instance, BridgeProtocol.SupportsQueryElements);
    }

    private static bool IsEligible(DiscoveredInstance instance, Func<int, bool> supportsCapability)
    {
        return instance.State == DiscoveryState.Ready
            && instance.Registration is not null
            && instance.Handshake is { } handshake
            && supportsCapability(handshake.SelectedProtocolVersion);
    }

    private static TargetResolution Resolve(
        IReadOnlyList<DiscoveredInstance> discovered,
        string? instanceId,
        Func<DiscoveredInstance, bool> isEligible)
    {
        ArgumentNullException.ThrowIfNull(discovered);

        if (instanceId is null)
        {
            return ResolveUnspecified(discovered, isEligible);
        }

        return ResolveExplicit(discovered, instanceId, isEligible);
    }

    private static TargetResolution ResolveUnspecified(
        IReadOnlyList<DiscoveredInstance> discovered,
        Func<DiscoveredInstance, bool> isEligible)
    {
        var eligible = discovered
            .Where(isEligible)
            .OrderBy(instance => instance.Registration!.InstanceId, StringComparer.Ordinal)
            .ToArray();

        if (eligible.Length == 0)
        {
            return TargetResolution.Failure(
                McpToolErrorCodes.NoRevitInstance,
                ToolErrorMessages.NoRevitInstance);
        }

        if (eligible.Length == 1)
        {
            return TargetResolution.Selected(eligible[0]);
        }

        return TargetResolution.Failure(
            McpToolErrorCodes.InstanceRequired,
            ToolErrorMessages.InstanceRequired,
            eligible.Select(ToCandidate).ToArray());
    }

    private static TargetResolution ResolveExplicit(
        IReadOnlyList<DiscoveredInstance> discovered,
        string instanceId,
        Func<DiscoveredInstance, bool> isEligible)
    {
        var match = discovered.FirstOrDefault(instance =>
            instance.Registration is not null
            && string.Equals(instance.Registration.InstanceId, instanceId, StringComparison.Ordinal));

        if (match is null)
        {
            return TargetResolution.Failure(
                McpToolErrorCodes.InstanceNotFound,
                ToolErrorMessages.InstanceNotFound);
        }

        if (!isEligible(match))
        {
            return TargetResolution.Failure(
                McpToolErrorCodes.InstanceUnavailable,
                ToolErrorMessages.InstanceUnavailable);
        }

        return TargetResolution.Selected(match);
    }

    private static InstanceCandidate ToCandidate(DiscoveredInstance instance)
    {
        var registration = instance.Registration!;
        return new InstanceCandidate
        {
            InstanceId = registration.InstanceId,
            RevitVersion = registration.RevitVersion,
            RevitBuild = registration.RevitBuild
        };
    }
}

internal sealed class TargetResolution
{
    private TargetResolution()
    {
    }

    public DiscoveredInstance? Target { get; private init; }

    public string? ErrorCode { get; private init; }

    public string? ErrorMessage { get; private init; }

    public IReadOnlyList<InstanceCandidate>? Candidates { get; private init; }

    public static TargetResolution Selected(DiscoveredInstance target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return new TargetResolution { Target = target };
    }

    public static TargetResolution Failure(
        string errorCode,
        string errorMessage,
        IReadOnlyList<InstanceCandidate>? candidates = null)
    {
        return new TargetResolution
        {
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            Candidates = candidates
        };
    }
}
