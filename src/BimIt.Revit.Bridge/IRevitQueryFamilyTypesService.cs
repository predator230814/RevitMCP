using BimIt.Revit.Contracts;

namespace BimIt.Revit.Bridge;

public interface IRevitQueryFamilyTypesService
{
    Task<QueryFamilyTypesResult> QueryFamilyTypesAsync(
        QueryFamilyTypesRequest request,
        CancellationToken cancellationToken);
}
