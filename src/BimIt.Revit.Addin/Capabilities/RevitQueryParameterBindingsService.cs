using Autodesk.Revit.UI;
using BimIt.Revit.Addin.Execution;
using BimIt.Revit.Addin.Identity;
using BimIt.Revit.Addin.Query;
using BimIt.Revit.Bridge;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Addin.Capabilities;

/// <summary>
/// CAP-0015 <c>revit.query_parameter_bindings</c>. Read-only: the query is prepared
/// (document guards, defined tokens, cursor, revision) before <c>Document.ParameterBindings</c>
/// is traversed, all inside one EXEC-0001 work item. No transaction is opened.
/// </summary>
internal sealed class RevitQueryParameterBindingsService : IRevitQueryParameterBindingsService
{
    private readonly RevitExecutionDispatcher _dispatcher;
    private readonly BridgeInstanceMetadata _metadata;
    private readonly OpenDocumentIdentityService _identity;
    private readonly OpenDocumentParameterIdentityService _parameterRefs;
    private readonly DocumentQueryRevisionService _revisions;
    private readonly DeclarativeQueryCursorCodec _cursors;
    private readonly IBuiltInCategoryCatalog _categories;

    public RevitQueryParameterBindingsService(
        RevitExecutionDispatcher dispatcher,
        BridgeInstanceMetadata metadata,
        OpenDocumentIdentityService identity,
        OpenDocumentParameterIdentityService parameterRefs,
        DocumentQueryRevisionService revisions,
        DeclarativeQueryCursorCodec cursors,
        IBuiltInCategoryCatalog categories)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(parameterRefs);
        ArgumentNullException.ThrowIfNull(revisions);
        ArgumentNullException.ThrowIfNull(cursors);
        ArgumentNullException.ThrowIfNull(categories);
        _dispatcher = dispatcher;
        _metadata = metadata;
        _identity = identity;
        _parameterRefs = parameterRefs;
        _revisions = revisions;
        _cursors = cursors;
        _categories = categories;
    }

    public async Task<QueryParameterBindingsResult> QueryParameterBindingsAsync(
        QueryParameterBindingsRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var validated = ParameterBindingQueryValidator.Validate(request);
        try
        {
            return await _dispatcher.EnqueueAsync(application => Execute(application, validated), cancellationToken).ConfigureAwait(false);
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
            throw ParameterBindingInventoryQuery.Unreadable();
        }
    }

    private QueryParameterBindingsResult Execute(UIApplication application, ValidatedParameterBindingQuery query)
    {
        var document = application.ActiveUIDocument?.Document;
        if (document is null)
        {
            throw new BridgeException(CapabilityErrorCodes.NoActiveDocument, "No active Revit document is available.");
        }

        if (document.IsFamilyDocument)
        {
            throw new BridgeException(CapabilityErrorCodes.UnsupportedDocumentKind, "The active document is not a project document.");
        }

        var documentId = _identity.GetId(document);
        if (!string.Equals(query.DocumentId, documentId, StringComparison.Ordinal))
        {
            throw new BridgeException(
                CapabilityErrorCodes.DocumentContextChanged,
                "The supplied document_id does not match the active document.");
        }

        var revision = _revisions.Current(documentId);
        var preparation = ParameterBindingInventoryQuery.Prepare(
            query,
            _metadata.InstanceId,
            documentId,
            hasActiveDocument: true,
            isFamilyDocument: false,
            revision,
            _categories,
            _cursors);
        var candidates = RevitParameterBindingReader.Read(document, _parameterRefs);
        return ParameterBindingInventoryQuery.Complete(preparation, query, candidates, _cursors);
    }
}
