using Autodesk.Revit.DB;

namespace RevitMCP.Addin;

internal sealed class ElementBasicMetadata
{
    public string? Name { get; init; }

    public string? CategoryName { get; init; }

    public string? FamilyName { get; init; }

    public string? TypeName { get; init; }

    public string? LevelName { get; init; }
}

internal sealed class ElementBasicMetadataResolver
{
    private readonly Document _document;
    private readonly Dictionary<ElementId, ElementType?> _typeCache = new();
    private readonly Dictionary<ElementId, Level?> _levelCache = new();

    public ElementBasicMetadataResolver(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        _document = document;
    }

    public ElementBasicMetadata Read(Element element)
    {
        ArgumentNullException.ThrowIfNull(element);
        var type = ResolveType(element.GetTypeId());
        var level = ResolveLevel(element.LevelId);

        return new ElementBasicMetadata
        {
            Name = NullIfEmpty(element.Name),
            CategoryName = NullIfEmpty(element.Category?.Name),
            FamilyName = NullIfEmpty(type?.FamilyName),
            TypeName = NullIfEmpty(type?.Name),
            LevelName = NullIfEmpty(level?.Name)
        };
    }

    public ElementType? ResolveType(Element element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return ResolveType(element.GetTypeId());
    }

    private ElementType? ResolveType(ElementId typeId)
    {
        if (typeId == ElementId.InvalidElementId)
        {
            return null;
        }

        if (_typeCache.TryGetValue(typeId, out var cached))
        {
            return cached;
        }

        var type = _document.GetElement(typeId) as ElementType;
        _typeCache[typeId] = type;
        return type;
    }

    private Level? ResolveLevel(ElementId levelId)
    {
        if (levelId == ElementId.InvalidElementId)
        {
            return null;
        }

        if (_levelCache.TryGetValue(levelId, out var cached))
        {
            return cached;
        }

        var level = _document.GetElement(levelId) as Level;
        _levelCache[levelId] = level;
        return level;
    }

    internal static string? NullIfEmpty(string? value) =>
        string.IsNullOrEmpty(value) ? null : value;
}
