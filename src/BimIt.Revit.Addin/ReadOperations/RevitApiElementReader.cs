using Autodesk.Revit.DB;
using BimIt.Revit.Bridge;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Addin.ReadOperations;

/// <summary>
/// Collects one concrete Element subclass with OfClass. A false modifiable flag is not proof
/// that no open change exists. element_ref reuses the value the element-query path assigns.
/// </summary>
internal static class RevitApiElementReader
{
    public const int MaxMatchedElements = 2000;

    public static IReadOnlyList<Element> Collect(Document document, Type elementType)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(elementType);
        if (document.IsModifiable)
        {
            throw new BridgeException(CapabilityErrorCodes.DocumentIsModifiable, "The document is modifiable.");
        }

        IList<Element> collected;
        try
        {
            using var collector = new FilteredElementCollector(document);
            collected = collector.OfClass(elementType).ToElements();
        }
        catch (Autodesk.Revit.Exceptions.ArgumentException exception) when (IndicatesNativeModelMismatch(exception.Message))
        {
            throw RevitApiTypeResolver.Reject("not_collectable");
        }

        if (document.IsModifiable)
        {
            throw new BridgeException(CapabilityErrorCodes.DocumentBecameModifiable, "The document became modifiable.");
        }

        if (collected.Count > MaxMatchedElements)
        {
            throw new BridgeException(CapabilityErrorCodes.ResultTooLarge, "The read operation result is too large.");
        }

        var elements = new List<Element>(collected.Count);
        foreach (var element in collected)
        {
            if (element is null)
            {
                continue;
            }

            if (string.IsNullOrEmpty(element.UniqueId))
            {
                throw new BridgeException(CapabilityErrorCodes.ExecutionFailed, "The element reference could not be read.");
            }

            elements.Add(element);
        }

        elements.Sort(static (left, right) => string.CompareOrdinal(left.UniqueId, right.UniqueId));
        return elements;
    }

    public static string ElementRef(Element element)
    {
        ArgumentNullException.ThrowIfNull(element);
        var elementRef = element.UniqueId;
        if (string.IsNullOrEmpty(elementRef))
        {
            throw new BridgeException(CapabilityErrorCodes.ExecutionFailed, "The element reference could not be read.");
        }

        return elementRef;
    }

    public static void ThrowIfBecameModifiable(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.IsModifiable)
        {
            throw new BridgeException(CapabilityErrorCodes.DocumentBecameModifiable, "The document became modifiable.");
        }
    }

    public static bool IndicatesNativeModelMismatch(string? message)
    {
        return message is not null
            && message.Contains("native object model", StringComparison.OrdinalIgnoreCase);
    }
}
