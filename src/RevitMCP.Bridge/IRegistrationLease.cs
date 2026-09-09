using RevitMCP.Contracts;

namespace RevitMCP.Bridge;

public interface IRegistrationLease : IAsyncDisposable, IDisposable
{
    RevitInstanceRegistration Registration { get; }
}
