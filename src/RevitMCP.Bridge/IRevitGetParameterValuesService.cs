using RevitMCP.Contracts;

namespace RevitMCP.Bridge;

public interface IRevitGetParameterValuesService
{
    Task<GetParameterValuesResult> GetParameterValuesAsync(
        GetParameterValuesRequest request,
        CancellationToken cancellationToken);
}
