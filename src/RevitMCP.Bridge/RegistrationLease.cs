using RevitMCP.Contracts;

namespace RevitMCP.Bridge;

internal sealed class RegistrationLease : IRegistrationLease
{
    private readonly IRegistrationStore _store;
    private int _disposed;

    public RegistrationLease(IRegistrationStore store, RevitInstanceRegistration registration)
    {
        _store = store;
        Registration = registration;
    }

    public RevitInstanceRegistration Registration { get; }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _store.RemoveAsync(Registration.WindowsSessionId, Registration.InstanceId, CancellationToken.None).ConfigureAwait(false);
    }
}
