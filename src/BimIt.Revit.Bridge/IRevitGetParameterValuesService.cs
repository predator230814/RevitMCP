using BimIt.Revit.Contracts;

namespace BimIt.Revit.Bridge;

public interface IRevitGetParameterValuesService
{
    Task<GetParameterValuesResult> GetParameterValuesAsync(
        GetParameterValuesRequest request,
        CancellationToken cancellationToken);
}
