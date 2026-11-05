using System.Globalization;
using Autodesk.Revit.DB;
using BimIt.Revit.Addin.Identity;
using BimIt.Revit.Addin.Inspection;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Addin.Query;

/// <summary>
/// CAP-0015 source reader: every entry of <c>Document.ParameterBindings</c> of the given host
/// project document, traversed completely with its forward iterator. Read-only: no transaction,
/// no link, no <c>ParameterElement</c> collector, no shared-parameter file. Must run inside an
/// EXEC-0001 work item, after the query has been prepared.
/// </summary>
internal static class RevitParameterBindingReader
{
    public static List<ParameterBindingCandidate> Read(
        Document document,
        OpenDocumentParameterIdentityService parameterRefs)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(parameterRefs);

        var bindings = document.ParameterBindings;
        var expected = bindings.Size;
        var rows = new List<ParameterBindingCandidate>(Math.Max(expected, 0));
        var iterator = bindings.ForwardIterator();
        iterator.Reset();
        while (iterator.MoveNext())
        {
            if (iterator.Key is not InternalDefinition definition)
            {
                throw ParameterBindingInventoryQuery.Unreadable();
            }

            QueryParameterBindingKind bindingKind;
            GetElementParameterSource source;
            ElementBinding elementBinding;
            switch (iterator.Current)
            {
                case InstanceBinding instanceBinding:
                    bindingKind = QueryParameterBindingKind.Instance;
                    source = GetElementParameterSource.Instance;
                    elementBinding = instanceBinding;
                    break;
                case TypeBinding typeBinding:
                    bindingKind = QueryParameterBindingKind.Type;
                    source = GetElementParameterSource.Type;
                    elementBinding = typeBinding;
                    break;
                default:
                    throw ParameterBindingInventoryQuery.Unreadable();
            }

            var identity = RevitParameterIdentity.Classify(definition, document);
            var dataType = RevitParameterIdentity.ClassifyDataType(definition);
            var parameterRef = parameterRefs.GetRef(document, source, identity);
            rows.Add(new ParameterBindingCandidate(
                parameterRef,
                definition.Name ?? string.Empty,
                identity,
                dataType,
                bindingKind,
                ReadCategories(elementBinding, rows.Count)));
        }

        // The whole map must have been traversed; a short traversal is never reported as complete.
        if (rows.Count != expected)
        {
            throw ParameterBindingInventoryQuery.Unreadable();
        }

        return rows;
    }

    private static List<ParameterBindingCategoryCandidate> ReadCategories(ElementBinding binding, int entry)
    {
        var categories = new List<ParameterBindingCategoryCandidate>();
        var set = binding.Categories;
        if (set is null)
        {
            return categories;
        }

        var position = 0;
        foreach (var item in set)
        {
            var category = item as Category;
            categories.Add(new ParameterBindingCategoryCandidate(
                RevitCategoryTokens.Read(category),
                ReadName(category),
                CategoryKey(category, entry, position)));
            position++;
        }

        return categories;
    }

    private static string ReadName(Category? category)
    {
        if (category is null)
        {
            return string.Empty;
        }

        try
        {
            return category.Name ?? string.Empty;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Internal category identity: the category element id, so two distinct categories that
    /// both map to <c>unknown</c> stay distinct. A null or unreadable entry gets a key unique
    /// to its binding entry and position, so it is kept and counted, never merged or dropped. Never returned.
    /// </summary>
    private static string CategoryKey(Category? category, int entry, int position)
    {
        if (category is not null)
        {
            try
            {
                var id = category.Id;
                if (id is not null && id != ElementId.InvalidElementId)
                {
                    return "id:" + id.Value.ToString(CultureInfo.InvariantCulture);
                }
            }
            catch (Exception)
            {
                // Falls through to a positional key.
            }
        }

        return "entry:" + entry.ToString(CultureInfo.InvariantCulture) + ":" + position.ToString(CultureInfo.InvariantCulture);
    }
}
