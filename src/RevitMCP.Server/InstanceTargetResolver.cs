using RevitMCP.Contracts;

namespace RevitMCP.Server;

internal static class InstanceTargetResolver
{
    public static TargetResolution Resolve(IReadOnlyList<DiscoveredInstance> discovered, string? instanceId)
    {
        ArgumentNullException.ThrowIfNull(discovered);

        if (string.IsNullOrWhiteSpace(instanceId))
        {
            return ResolveUnspecified(discovered);
        }

        return ResolveExplicit(discovered, instanceId);
    }

    public static bool IsGetContextEligible(DiscoveredInstance instance)
    {
        return instance.State == DiscoveryState.Ready
            && instance.Registration is not null
            && instance.Handshake is { SelectedProtocolVersion: BridgeProtocol.GetContextVersion };
    }

    private static TargetResolution ResolveUnspecified(IReadOnlyList<DiscoveredInstance> discovered)
    {
        var eligible = discovered
            .Where(IsGetContextEligible)
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

    private static TargetResolution ResolveExplicit(IReadOnlyList<DiscoveredInstance> discovered, string instanceId)
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

        if (!IsGetContextEligible(match))
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
