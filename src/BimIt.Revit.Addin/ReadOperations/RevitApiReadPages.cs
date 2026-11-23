using BimIt.Revit.Addin.Query;
using BimIt.Revit.Bridge;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Addin.ReadOperations;

internal static class RevitApiReadPages
{
    public static ReadOperationExecutionResult Complete<T>(
        ReadOperationExecutionContext context,
        int matchedCount,
        IReadOnlyList<T> available,
        Func<IReadOnlyList<T>, object> shape,
        byte[] fingerprint)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(available);
        ArgumentNullException.ThrowIfNull(shape);
        ArgumentNullException.ThrowIfNull(fingerprint);
        if (matchedCount < 0 || available.Count > context.PageSize)
        {
            throw new BridgeException(CapabilityErrorCodes.ExecutionFailed, "The read operation could not be read.");
        }

        if (available.Count == 0)
        {
            return Finish(context, shape([]), complete: true, cursor: null);
        }

        for (var count = available.Count; count >= 1; count--)
        {
            var end = context.Offset + count;
            var finalPage = end >= matchedCount;
            string? cursor = null;
            if (!finalPage)
            {
                var payload = new DeclarativeQueryCursorPayload(
                    context.Descriptor.Id,
                    context.InstanceId,
                    context.DocumentId,
                    fingerprint,
                    context.Revision,
                    end);
                if (!context.Cursors.TryMint(payload, out var minted))
                {
                    throw new BridgeException(CapabilityErrorCodes.ExecutionFailed, "The read operation could not be read.");
                }

                cursor = minted;
            }

            var typed = shape(available.Take(count).ToArray());
            var envelope = ReadOperationResults.Envelope(
                context.InstanceId,
                context.DocumentId,
                context.Descriptor.Id,
                context.Descriptor.ContractVersion,
                ReadOperationResults.Payload(typed),
                finalPage,
                cursor);
            if (ReadResultBudget.Measure(envelope) <= ReadResultBudget.MaxCharacters)
            {
                return new ReadOperationExecutionResult
                {
                    TypedResult = typed,
                    Complete = finalPage,
                    NextCursor = cursor
                };
            }
        }

        throw new BridgeException(CapabilityErrorCodes.ResultTooLarge, "The read operation result is too large.");
    }

    public static IReadOnlyList<T> Slice<T>(IReadOnlyList<T> matched, int offset, int pageSize)
    {
        ArgumentNullException.ThrowIfNull(matched);
        if (offset < 0 || offset > matched.Count || (offset == matched.Count && matched.Count > 0))
        {
            throw new BridgeException(CapabilityErrorCodes.InvalidCursor, "The read operation cursor is not valid.");
        }

        var rows = new List<T>();
        for (var index = offset; index < matched.Count && rows.Count < pageSize; index++)
        {
            rows.Add(matched[index]);
        }

        return rows;
    }

    private static ReadOperationExecutionResult Finish(
        ReadOperationExecutionContext context,
        object typed,
        bool complete,
        string? cursor)
    {
        var envelope = ReadOperationResults.Envelope(
            context.InstanceId,
            context.DocumentId,
            context.Descriptor.Id,
            context.Descriptor.ContractVersion,
            ReadOperationResults.Payload(typed),
            complete,
            cursor);
        if (ReadResultBudget.Measure(envelope) > ReadResultBudget.MaxCharacters)
        {
            throw new BridgeException(CapabilityErrorCodes.ResultTooLarge, "The read operation result is too large.");
        }

        return new ReadOperationExecutionResult
        {
            TypedResult = typed,
            Complete = complete,
            NextCursor = cursor
        };
    }
}
