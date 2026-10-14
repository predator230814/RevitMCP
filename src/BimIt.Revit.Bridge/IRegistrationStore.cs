using BimIt.Revit.Contracts;

namespace BimIt.Revit.Bridge;

public interface IRegistrationStore
{
    string RootPath { get; }

    string GetRegistrationPath(int windowsSessionId, string instanceId);

    Task<IRegistrationLease> PublishAsync(RevitInstanceRegistration registration, CancellationToken cancellationToken);

    Task<IReadOnlyList<RegistrationReadResult>> ReadSessionAsync(int windowsSessionId, CancellationToken cancellationToken);

    Task RemoveAsync(int windowsSessionId, string instanceId, CancellationToken cancellationToken);
}
