namespace RevitMCP.Addin.Inspection;

internal sealed class ElementInspectionCandidate
{
    public required string ElementRef { get; init; }

    public string? Name { get; init; }

    public string? CategoryName { get; init; }

    public string? FamilyName { get; init; }

    public string? TypeName { get; init; }

    public string? LevelName { get; init; }

    public IReadOnlyList<ParameterInspectionCandidate> InstanceParameters { get; init; } = [];

    public IReadOnlyList<ParameterInspectionCandidate> TypeParameters { get; init; } = [];
}
