using BimIt.Revit.Contracts;

namespace BimIt.Revit.Addin.Query;

/// <summary>
/// Chooses the instance or type surface using the same source rule as
/// <c>revit_get_parameter_values</c>. A type source with no resolvable type is missing.
/// </summary>
internal static class QueryParameterSurfaceResolver
{
    public static QueryParameterSurface Resolve(
        GetElementParameterSource source,
        Func<QueryParameterSurface> readInstance,
        Func<QueryParameterSurface?> readType)
    {
        ArgumentNullException.ThrowIfNull(readInstance);
        ArgumentNullException.ThrowIfNull(readType);

        if (source == GetElementParameterSource.Type)
        {
            return readType() ?? QueryParameterSurface.Missing;
        }

        return readInstance();
    }
}
