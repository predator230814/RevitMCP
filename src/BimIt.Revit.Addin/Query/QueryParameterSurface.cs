namespace BimIt.Revit.Addin.Query;

internal enum QueryParameterStorage
{
    None,
    String,
    Integer,
    Double,
    ElementId
}

internal sealed class QueryParameterSurface
{
    public static QueryParameterSurface Missing { get; } = new() { Present = false };

    public required bool Present { get; init; }

    public bool HasValue { get; init; }

    public QueryParameterStorage Storage { get; init; }

    public string? StringValue { get; init; }

    public int IntegerValue { get; init; }

    public double DoubleValue { get; init; }

    public string? SpecTypeId { get; init; }

    public bool MeasurableSpec { get; init; }
}
