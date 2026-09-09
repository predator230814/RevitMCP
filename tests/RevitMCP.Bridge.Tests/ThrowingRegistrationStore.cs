using RevitMCP.Contracts;

namespace RevitMCP.Bridge.Tests;

internal sealed class ThrowingRegistrationStore : IRegistrationStore
{
    public string RootPath { get; } = Path.Combine(Path.GetTempPath(), "RevitMCP.Tests", "unused");

    public string GetRegistrationPath(int windowsSessionId, string instanceId)
    {
        return Path.Combine(RootPath, windowsSessionId.ToString(), $"{instanceId}.json");
    }

    public Task<IRegistrationLease> PublishAsync(RevitInstanceRegistration registration, CancellationToken cancellationToken)
    {
        _ = registration;
        _ = cancellationToken;
        throw new InvalidOperationException("Registration publish failed.");
    }

    public Task<IReadOnlyList<RegistrationReadResult>> ReadSessionAsync(int windowsSessionId, CancellationToken cancellationToken)
    {
        _ = windowsSessionId;
        _ = cancellationToken;
        return Task.FromResult<IReadOnlyList<RegistrationReadResult>>([]);
    }

    public Task RemoveAsync(int windowsSessionId, string instanceId, CancellationToken cancellationToken)
    {
        _ = windowsSessionId;
        _ = instanceId;
        _ = cancellationToken;
        return Task.CompletedTask;
    }
}
