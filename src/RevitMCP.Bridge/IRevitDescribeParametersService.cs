using RevitMCP.Contracts;

namespace RevitMCP.Bridge;

public interface IRevitDescribeParametersService
{
    Task<DescribeParametersResult> DescribeParametersAsync(
        DescribeParametersRequest request,
        CancellationToken cancellationToken);
}
