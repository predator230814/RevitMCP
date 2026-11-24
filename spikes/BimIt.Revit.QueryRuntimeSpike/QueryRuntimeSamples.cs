namespace BimIt.Revit.QueryRuntimeSpike;

public static class QueryRuntimeSamples
{
    public const string WallCollector = """
        var elements = new FilteredElementCollector(doc)
            .OfClass(typeof(Wall))
            .WhereElementIsNotElementType()
            .ToElements();
        return elements.Select(element => new { id = elementRef(element), name = element.Name }).ToArray();
        """;

    public const string LevelProjection = """
        var levels = new FilteredElementCollector(doc)
            .OfClass(typeof(Level))
            .ToElements();
        return new
        {
            count = levels.Count,
            sample = levels.Take(3).Select(level => new { id = elementRef(level), name = level.Name }).ToArray()
        };
        """;

    public const string TightLoop = """
        while (true)
        {
        }
        return 0;
        """;

    public const string TransactionAttempt = """
        return new Transaction(doc, "spike");
        """;
}
