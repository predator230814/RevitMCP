using BimIt.Revit.Addin.Query;
using BimIt.Revit.Bridge;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Addin.ReadOperations;

/// <summary>
/// Builds one registry and the Bridge service that shares it. Registration failure returns no service.
/// </summary>
internal static class ReadOperationComposition
{
    public static IReadOnlyList<IExecutableReadOperation> ProductionOperations()
    {
        return [new ProjectUserWorksetsReadOperation()];
    }

    public static IRevitReadOperationsService? TryCreate(
        IReadOnlyList<IExecutableReadOperation> operations,
        string instanceId,
        DeclarativeQueryCursorCodec cursors,
        DocumentQueryRevisionService revisions,
        IReadOperationWorkQueue queue)
    {
        ArgumentNullException.ThrowIfNull(operations);
        try
        {
            var registry = new ReadOperationRegistry(operations);
            var lookup = new RevitLookupReadOperationsService(registry);
            var pipeline = new ReadOperationPipeline(registry, instanceId, cursors, revisions);
            var read = new RevitReadOperationService(pipeline, queue);
            return new RevitReadOperationsBridgeService(lookup, read);
        }
        catch (ReadOperationRegistrationException)
        {
            return null;
        }
    }
}

internal sealed class RevitReadOperationsBridgeService : IRevitReadOperationsService
{
    private readonly RevitLookupReadOperationsService _lookup;
    private readonly RevitReadOperationService _read;

    public RevitReadOperationsBridgeService(
        RevitLookupReadOperationsService lookup,
        RevitReadOperationService read)
    {
        ArgumentNullException.ThrowIfNull(lookup);
        ArgumentNullException.ThrowIfNull(read);
        _lookup = lookup;
        _read = read;
    }

    public Task<LookupReadOperationsResult> LookupReadOperationsAsync(
        LookupReadOperationsRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_lookup.Lookup(request));
    }

    public Task<ReadOperationResult> ReadOperationAsync(
        ReadOperationRequest request,
        CancellationToken cancellationToken)
    {
        return _read.ReadAsync(request, cancellationToken);
    }
}
