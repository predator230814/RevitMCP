namespace RevitMCP.Addin.Query;

internal sealed class QueryElementCandidate
{
    public required string ElementRef { get; init; }

    public string? ElementName { get; init; }

    public string? CategoryName { get; init; }

    public string? FamilyName { get; init; }

    public string? TypeName { get; init; }

    public string? LevelName { get; init; }
}
