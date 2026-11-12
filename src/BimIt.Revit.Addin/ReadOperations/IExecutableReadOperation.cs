using System.Text.Json;
using BimIt.Revit.Addin.Query;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Addin.ReadOperations;

internal interface IExecutableReadOperation : IRevitReadOperation
{
    object Bind(JsonElement inputs);

    void ValidateInputs(object request);

    byte[] Fingerprint(object request, string documentId, ReadOperationDescriptor descriptor);

    ReadOperationExecutionResult Execute(object request, ReadOperationExecutionContext context);

    void ValidateResult(object result);
}

internal sealed class ReadOperationExecutionContext
{
    public required ReadOperationDescriptor Descriptor { get; init; }

    public required string InstanceId { get; init; }

    public required string DocumentId { get; init; }

    public required int PageSize { get; init; }

    public required int Offset { get; init; }

    public required long Revision { get; init; }

    public required DeclarativeQueryCursorCodec Cursors { get; init; }

    public required IReadOperationDocument Host { get; init; }
}

internal sealed class ReadOperationExecutionResult
{
    public required object TypedResult { get; init; }

    public required bool Complete { get; init; }

    public required string? NextCursor { get; init; }
}

internal interface IReadOperationDocument
{
    bool HasActiveDocument { get; }

    bool IsFamilyDocument { get; }

    string GetDocumentId();
}
