using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BimIt.Revit.Addin.Execution;
using BimIt.Revit.Addin.Identity;
using BimIt.Revit.Bridge;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Addin.ReadOperations;

internal interface IReadOperationWorkQueue
{
    Task<ReadOperationResult> EnqueueAsync(
        Func<IReadOperationDocument, ReadOperationResult> execute,
        CancellationToken cancellationToken);
}

internal sealed class RevitReadOperationService
{
    private readonly ReadOperationPipeline _pipeline;
    private readonly IReadOperationWorkQueue _queue;

    public RevitReadOperationService(ReadOperationPipeline pipeline, IReadOperationWorkQueue queue)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(queue);
        _pipeline = pipeline;
        _queue = queue;
    }

    public async Task<ReadOperationResult> ReadAsync(ReadOperationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var prepared = _pipeline.Prepare(request);
        try
        {
            return await _queue.EnqueueAsync(document => _pipeline.Execute(prepared, document), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (BridgeException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new BridgeException(CapabilityErrorCodes.ExecutionFailed, "The read operation could not be read.");
        }
    }
}

internal sealed class RevitReadOperationWorkQueue : IReadOperationWorkQueue
{
    private readonly RevitExecutionDispatcher _dispatcher;
    private readonly OpenDocumentIdentityService _identity;
    private readonly RevitUserWorksetReader _reader;

    public RevitReadOperationWorkQueue(
        RevitExecutionDispatcher dispatcher,
        OpenDocumentIdentityService identity,
        RevitUserWorksetReader reader)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(reader);
        _dispatcher = dispatcher;
        _identity = identity;
        _reader = reader;
    }

    public Task<ReadOperationResult> EnqueueAsync(
        Func<IReadOperationDocument, ReadOperationResult> execute,
        CancellationToken cancellationToken)
    {
        return _dispatcher.EnqueueAsync(
            application => execute(new RevitActiveReadDocument(application, _identity, _reader)),
            cancellationToken);
    }
}

internal sealed class RevitActiveReadDocument : IReadOperationDocument
{
    private readonly Document? _document;
    private readonly OpenDocumentIdentityService _identity;
    private readonly RevitUserWorksetReader _reader;

    public RevitActiveReadDocument(
        UIApplication application,
        OpenDocumentIdentityService identity,
        RevitUserWorksetReader reader)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(reader);
        _document = application.ActiveUIDocument?.Document;
        _identity = identity;
        _reader = reader;
    }

    public bool HasActiveDocument => _document is not null;

    public bool IsFamilyDocument => _document?.IsFamilyDocument == true;

    public string GetDocumentId()
    {
        return _identity.GetId(_document!);
    }

    public UserWorksetInventory ReadUserWorksets()
    {
        return _reader.Read(_document!);
    }
}
