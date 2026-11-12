using System.Text.Json;
using BimIt.Revit.Addin.Query;
using BimIt.Revit.Bridge;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Addin.ReadOperations;

internal sealed class PreparedReadOperation
{
    public required IExecutableReadOperation Operation { get; init; }

    public required ReadOperationDescriptor Descriptor { get; init; }

    public required object Request { get; init; }

    public required string DocumentId { get; init; }

    public required int PageSize { get; init; }

    public required DeclarativeQueryCursorPayload? Cursor { get; init; }
}

internal sealed class ReadOperationPipeline
{
    public const int DefaultPageSize = 100;

    public const int MaxPageSize = 200;

    private readonly ReadOperationRegistry _registry;
    private readonly string _instanceId;
    private readonly DeclarativeQueryCursorCodec _cursors;
    private readonly DocumentQueryRevisionService _revisions;

    public ReadOperationPipeline(
        ReadOperationRegistry registry,
        string instanceId,
        DeclarativeQueryCursorCodec cursors,
        DocumentQueryRevisionService revisions)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrEmpty(instanceId);
        ArgumentNullException.ThrowIfNull(cursors);
        ArgumentNullException.ThrowIfNull(revisions);
        _registry = registry;
        _instanceId = instanceId;
        _cursors = cursors;
        _revisions = revisions;
    }

    public PreparedReadOperation Prepare(ReadOperationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ContractVersion is not int contractVersion)
        {
            throw InvalidInput("contract_version is required.");
        }

        var resolved = _registry.Resolve(request.Operation, contractVersion);
        if (resolved.Resolution == ReadOperationResolution.UnknownOperation)
        {
            throw new BridgeException(CapabilityErrorCodes.UnknownReadOperation, "No read operation with this id is registered.");
        }

        if (resolved.Resolution == ReadOperationResolution.UnsupportedVersion)
        {
            throw new BridgeException(
                CapabilityErrorCodes.ReadOperationVersionUnsupported,
                "The requested contract version is not registered. Registered versions: " + string.Join(", ", resolved.RegisteredVersions) + ".");
        }

        var descriptor = resolved.Descriptor ?? throw Failed();
        var operation = resolved.Operation as IExecutableReadOperation ?? throw Failed();
        var pageSize = AcceptPaging(descriptor, request);
        if (string.IsNullOrEmpty(request.DocumentId))
        {
            throw InvalidInput("document_id is required.");
        }

        if (request.Inputs is not { } inputs || inputs.ValueKind != JsonValueKind.Object)
        {
            throw InvalidInput(AcceptedInputs(descriptor));
        }

        if (!JsonSchemaSubset.MatchesInstance(inputs, descriptor.InputSchema!.Value))
        {
            throw InvalidInput(AcceptedInputs(descriptor));
        }

        object bound;
        try
        {
            bound = operation.Bind(inputs);
        }
        catch (BridgeException)
        {
            throw;
        }
        catch (JsonException)
        {
            throw InvalidInput(AcceptedInputs(descriptor));
        }
        catch (NotSupportedException)
        {
            throw InvalidInput(AcceptedInputs(descriptor));
        }

        operation.ValidateInputs(bound);
        var cursor = AcceptCursor(operation, bound, descriptor, request.DocumentId, request.Cursor);
        return new PreparedReadOperation
        {
            Operation = operation,
            Descriptor = descriptor,
            Request = bound,
            DocumentId = request.DocumentId,
            PageSize = pageSize,
            Cursor = cursor
        };
    }

    public ReadOperationResult Execute(PreparedReadOperation prepared, IReadOperationDocument document)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        ArgumentNullException.ThrowIfNull(document);
        if (!document.HasActiveDocument)
        {
            throw new BridgeException(CapabilityErrorCodes.NoActiveDocument, "No active Revit document is available.");
        }

        if (document.IsFamilyDocument)
        {
            throw new BridgeException(CapabilityErrorCodes.UnsupportedDocumentKind, "The active document is not a project document.");
        }

        var documentId = document.GetDocumentId();
        if (!string.Equals(prepared.DocumentId, documentId, StringComparison.Ordinal))
        {
            throw new BridgeException(
                CapabilityErrorCodes.DocumentContextChanged,
                "The supplied document_id does not match the active document.");
        }

        var revision = _revisions.Current(documentId);
        if (prepared.Cursor is { } cursor && cursor.Revision != revision)
        {
            throw new BridgeException(CapabilityErrorCodes.StaleCursor, "The read operation cursor is stale.");
        }

        ReadOperationExecutionResult page;
        try
        {
            page = prepared.Operation.Execute(prepared.Request, new ReadOperationExecutionContext
            {
                Descriptor = prepared.Descriptor,
                InstanceId = _instanceId,
                DocumentId = documentId,
                PageSize = prepared.PageSize,
                Offset = prepared.Cursor?.Offset ?? 0,
                Revision = revision,
                Cursors = _cursors,
                ReadWorksets = document.ReadUserWorksets
            });
        }
        catch (BridgeException)
        {
            throw;
        }
        catch (Exception)
        {
            throw Failed();
        }

        try
        {
            prepared.Operation.ValidateResult(page.TypedResult);
        }
        catch (BridgeException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new BridgeException(CapabilityErrorCodes.ReadOperationResultInvalid, "The read operation result is invalid.");
        }

        JsonElement payload;
        try
        {
            payload = ReadOperationResults.Payload(page.TypedResult);
        }
        catch (Exception)
        {
            throw Failed();
        }

        if (prepared.Descriptor.OutputSchema is not { } outputSchema
            || !JsonSchemaSubset.MatchesInstance(payload, outputSchema))
        {
            throw new BridgeException(CapabilityErrorCodes.ReadOperationResultInvalid, "The read operation result is invalid.");
        }

        var complete = page.Complete;
        var nextCursor = page.NextCursor;
        if (string.Equals(prepared.Descriptor.Pagination, "none", StringComparison.Ordinal))
        {
            complete = true;
            nextCursor = null;
        }

        var envelope = ReadOperationResults.Envelope(
            _instanceId,
            documentId,
            prepared.Descriptor.Id,
            prepared.Descriptor.ContractVersion,
            payload,
            complete,
            nextCursor);
        if (ReadResultBudget.Measure(envelope) > ReadResultBudget.MaxCharacters)
        {
            throw new BridgeException(CapabilityErrorCodes.ResultTooLarge, "The read operation result is too large.");
        }

        return envelope;
    }

    private DeclarativeQueryCursorPayload? AcceptCursor(
        IExecutableReadOperation operation,
        object request,
        ReadOperationDescriptor descriptor,
        string documentId,
        string? cursor)
    {
        if (cursor is null)
        {
            return null;
        }

        var fingerprint = operation.Fingerprint(request, documentId, descriptor);
        if (cursor.Length > DeclarativeQueryCursorCodec.MaxCursorLength
            || !_cursors.TryOpen(cursor, descriptor.Id, out var payload)
            || !string.Equals(payload.InstanceId, _instanceId, StringComparison.Ordinal)
            || !string.Equals(payload.DocumentId, documentId, StringComparison.Ordinal)
            || !payload.Fingerprint.AsSpan().SequenceEqual(fingerprint))
        {
            throw new BridgeException(CapabilityErrorCodes.InvalidCursor, "The read operation cursor is not valid.");
        }

        return payload;
    }

    private static int AcceptPaging(ReadOperationDescriptor descriptor, ReadOperationRequest request)
    {
        var cursorOperation = string.Equals(descriptor.Pagination, "cursor", StringComparison.Ordinal);
        if (!cursorOperation)
        {
            var rejected = new List<string>();
            if (request.PageSize is not null)
            {
                rejected.Add("page_size");
            }

            if (request.Cursor is not null)
            {
                rejected.Add("cursor");
            }

            if (rejected.Count > 0)
            {
                var verb = rejected.Count == 1 ? "is" : "are";
                throw InvalidInput(string.Join(" and ", rejected) + " " + verb + " not accepted by this operation.");
            }

            return 0;
        }

        if (request.PageSize is not int pageSize)
        {
            return DefaultPageSize;
        }

        if (pageSize is < 1 or > MaxPageSize)
        {
            throw InvalidInput("page_size is invalid.");
        }

        return pageSize;
    }

    private static string AcceptedInputs(ReadOperationDescriptor descriptor)
    {
        if (descriptor.InputSchema is not { } schema
            || !schema.TryGetProperty("properties", out var properties)
            || properties.ValueKind != JsonValueKind.Object)
        {
            return "The read operation inputs are invalid.";
        }

        var names = new List<string>();
        foreach (var property in properties.EnumerateObject())
        {
            names.Add(property.Name);
        }

        return names.Count == 0
            ? "The read operation inputs are invalid."
            : "The read operation inputs are invalid. Accepted inputs: " + string.Join(", ", names) + ".";
    }

    private static BridgeException InvalidInput(string message)
    {
        return new BridgeException(CapabilityErrorCodes.InvalidReadOperationInput, message);
    }

    private static BridgeException Failed()
    {
        return new BridgeException(CapabilityErrorCodes.ExecutionFailed, "The read operation could not be read.");
    }
}
