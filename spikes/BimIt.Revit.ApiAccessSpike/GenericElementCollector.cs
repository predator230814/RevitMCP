using Autodesk.Revit.DB;
using BimIt.Revit.Bridge;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Addin.ReadOperations;

/// <summary>
/// Collects instances of one Element subclass. The type argument is the caller's resolved type.
/// </summary>
internal static class GenericElementCollector
{
    public const int MaxMatchedElements = 2000;

    public static IReadOnlyList<Element> Collect(Document document, Type elementType)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(elementType);
        var collected = new FilteredElementCollector(document)
            .OfClass(elementType)
            .ToElements();
        if (collected.Count > MaxMatchedElements)
        {
            throw new BridgeException(
                CapabilityErrorCodes.ResultTooLarge,
                "The generic read matched more elements than the spike bound of " + MaxMatchedElements.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".");
        }

        var elements = new List<Element>(collected.Count);
        foreach (var element in collected)
        {
            if (element is not null)
            {
                elements.Add(element);
            }
        }

        elements.Sort(static (left, right) => string.CompareOrdinal(left.UniqueId, right.UniqueId));
        return elements;
    }
}
