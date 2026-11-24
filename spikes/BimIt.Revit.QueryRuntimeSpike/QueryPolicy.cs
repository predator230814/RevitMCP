namespace BimIt.Revit.QueryRuntimeSpike;

/// <summary>
/// Closed read-only policy for the spike. This is not an operating-system sandbox.
/// </summary>
public static class QueryPolicy
{
    public const int Version = 1;

    public const int MaxSourceCharacters = 8_000;

    public const int MaxResultCharacters = 16_000;

    public const int MaxDiagnostics = 8;

    public const int DefaultBudget = 10_000;

    public const string ContractRevitDocument = "revit-document";

    public const string ContractHostProbe = "host-probe";
}
