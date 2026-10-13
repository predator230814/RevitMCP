using BimIt.Revit.Contracts;

namespace BimIt.Revit.Bridge;

public interface IRevitDescribeParametersService
{
    Task<DescribeParametersResult> DescribeParametersAsync(
        DescribeParametersRequest request,
        CancellationToken cancellationToken);
}
