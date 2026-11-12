using BimIt.Revit.Contracts;

namespace BimIt.Revit.Bridge;

public interface IRevitReadOperationsService
{
    Task<LookupReadOperationsResult> LookupReadOperationsAsync(
        LookupReadOperationsRequest request,
        CancellationToken cancellationToken);

    Task<ReadOperationResult> ReadOperationAsync(
        ReadOperationRequest request,
        CancellationToken cancellationToken);
}
