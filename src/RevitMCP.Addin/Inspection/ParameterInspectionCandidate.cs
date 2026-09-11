namespace RevitMCP.Addin.Inspection;

internal sealed class ParameterInspectionCandidate
{
    public required string Name { get; init; }

    public string? ValueText { get; init; }
}
