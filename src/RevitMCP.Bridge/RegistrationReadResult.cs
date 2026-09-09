using RevitMCP.Contracts;

namespace RevitMCP.Bridge;

public sealed class RegistrationReadResult
{
    public required string FilePath { get; init; }

    public RevitInstanceRegistration? Registration { get; init; }

    public bool IsMalformed => Registration is null;
}
