using System.Text.Json;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Addin.ReadOperations;

internal static class ReadOperationResults
{
    public static JsonElement Payload(object typedResult)
    {
        var json = ReadResultBudget.Serialize(typedResult);
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    public static ReadOperationResult Envelope(
        string instanceId,
        string documentId,
        string operationId,
        int contractVersion,
        JsonElement result,
        bool complete,
        string? nextCursor)
    {
        return new ReadOperationResult
        {
            Context = new ReadOperationContext
            {
                InstanceId = instanceId,
                DocumentId = documentId
            },
            Operation = operationId,
            ContractVersion = contractVersion,
            Result = result,
            Complete = complete,
            NextCursor = nextCursor
        };
    }
}
