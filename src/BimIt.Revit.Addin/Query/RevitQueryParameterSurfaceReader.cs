using Autodesk.Revit.DB;
using BimIt.Revit.Addin.Identity;
using BimIt.Revit.Addin.Inspection;

namespace BimIt.Revit.Addin.Query;

/// <summary>
/// Reads one predicate parameter with the CAP-0005 resolution rule:
/// the opaque ref's source selects the instance or its type, and the match is
/// <see cref="RevitParameterIdentity.Classify"/> equality. No parameter fields
/// are copied onto <see cref="QueryElementCandidate"/>.
/// </summary>
internal static class RevitQueryParameterSurfaceReader
{
    public static QueryParameterSurface Read(
        Document document,
        Element element,
        ElementBasicMetadataResolver metadata,
        ParameterIdentityBinding binding)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(metadata);

        return QueryParameterSurfaceResolver.Resolve(
            binding.Source,
            () => ReadSurface(document, element, binding.Identity),
            () =>
            {
                var type = metadata.ResolveType(element);
                return type is null ? null : ReadSurface(document, type, binding.Identity);
            });
    }

    private static QueryParameterSurface ReadSurface(
        Document document,
        Element surface,
        ClassifiedParameterIdentity identity)
    {
        foreach (var parameter in surface.GetOrderedParameters())
        {
            if (RevitParameterIdentity.Classify(parameter, document) == identity)
            {
                return Capture(parameter);
            }
        }

        return QueryParameterSurface.Missing;
    }

    private static QueryParameterSurface Capture(Parameter parameter)
    {
        var spec = parameter.Definition?.GetDataType();
        var specTypeId = spec is null || spec.Empty() ? null : spec.TypeId;
        var measurable = spec is not null && !spec.Empty() && UnitUtils.IsMeasurableSpec(spec);
        var hasValue = parameter.HasValue;
        var storage = parameter.StorageType;

        return new QueryParameterSurface
        {
            Present = true,
            HasValue = hasValue,
            Storage = Map(storage),
            StringValue = hasValue && storage == StorageType.String ? parameter.AsString() : null,
            IntegerValue = hasValue && storage == StorageType.Integer ? parameter.AsInteger() : 0,
            DoubleValue = hasValue && storage == StorageType.Double ? parameter.AsDouble() : 0,
            SpecTypeId = specTypeId,
            MeasurableSpec = measurable
        };
    }

    private static QueryParameterStorage Map(StorageType storage)
    {
        return storage switch
        {
            StorageType.String => QueryParameterStorage.String,
            StorageType.Integer => QueryParameterStorage.Integer,
            StorageType.Double => QueryParameterStorage.Double,
            StorageType.ElementId => QueryParameterStorage.ElementId,
            _ => QueryParameterStorage.None
        };
    }
}
