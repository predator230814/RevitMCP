using BimIt.Revit.Contracts;

namespace BimIt.Revit.Bridge;

/// <summary>
/// CAP-0015 <c>revit.query_parameter_bindings</c> (BRIDGE-0019, protocol 23).
/// </summary>
public interface IRevitQueryParameterBindingsService
{
    Task<QueryParameterBindingsResult> QueryParameterBindingsAsync(
        QueryParameterBindingsRequest request,
        CancellationToken cancellationToken);
}
