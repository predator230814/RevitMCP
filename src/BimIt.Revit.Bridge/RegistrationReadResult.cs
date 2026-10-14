using BimIt.Revit.Contracts;

namespace BimIt.Revit.Bridge;

public sealed class RegistrationReadResult
{
    public required string FilePath { get; init; }

    public RevitInstanceRegistration? Registration { get; init; }

    public bool IsMalformed => Registration is null;
}
