using BimIt.Revit.Addin.Query;
using BimIt.Revit.Bridge;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Addin.ReadOperations;

internal static class ProjectUserWorksetsQuery
{
    public static ReadOperationExecutionResult Complete(
        UserWorksetsRequest request,
        ReadOperationExecutionContext context,
        UserWorksetInventory inventory)
    {
        if (!inventory.WorksharingEnabled)
        {
            return Empty(context, worksharingEnabled: false);
        }

        var matched = new List<UserWorksetCandidate>();
        foreach (var candidate in inventory.Worksets)
        {
            if (request.NameContains is null
                || candidate.Name.Contains(request.NameContains, StringComparison.OrdinalIgnoreCase))
            {
                matched.Add(candidate);
            }
        }

        matched.Sort(static (left, right) =>
        {
            var ignoreCase = StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name);
            return ignoreCase != 0
                ? ignoreCase
                : StringComparer.Ordinal.Compare(left.Name, right.Name);
        });

        var offset = context.Offset;
        if (offset > matched.Count || (offset == matched.Count && matched.Count > 0))
        {
            throw new BridgeException(CapabilityErrorCodes.InvalidCursor, "The read operation cursor is not valid.");
        }

        var summary = new UserWorksetsSummary
        {
            WorksharingEnabled = true,
            MatchedWorksetCount = matched.Count
        };
        var available = matched.Skip(offset).Take(context.PageSize).ToList();
        if (available.Count == 0)
        {
            return Empty(context, worksharingEnabled: true);
        }

        var fingerprint = ProjectUserWorksetsReadOperation.FingerprintFor(request, context.DocumentId, context.Descriptor);
        for (var count = available.Count; count >= 1; count--)
        {
            var end = offset + count;
            var finalPage = end >= matched.Count;
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

            var typed = Shape(summary, available.Take(count).ToArray());
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

    private static ReadOperationExecutionResult Empty(ReadOperationExecutionContext context, bool worksharingEnabled)
    {
        var typed = Shape(
            new UserWorksetsSummary
            {
                WorksharingEnabled = worksharingEnabled,
                MatchedWorksetCount = 0
            },
            []);
        var envelope = ReadOperationResults.Envelope(
            context.InstanceId,
            context.DocumentId,
            context.Descriptor.Id,
            context.Descriptor.ContractVersion,
            ReadOperationResults.Payload(typed),
            true,
            null);
        if (ReadResultBudget.Measure(envelope) > ReadResultBudget.MaxCharacters)
        {
            throw new BridgeException(CapabilityErrorCodes.ResultTooLarge, "The read operation result is too large.");
        }

        return new ReadOperationExecutionResult
        {
            TypedResult = typed,
            Complete = true,
            NextCursor = null
        };
    }

    private static UserWorksetsResult Shape(UserWorksetsSummary summary, IReadOnlyList<UserWorksetCandidate> rows)
    {
        var worksets = new UserWorksetRow[rows.Count];
        for (var index = 0; index < rows.Count; index++)
        {
            worksets[index] = new UserWorksetRow
            {
                Name = rows[index].Name,
                IsDefault = rows[index].IsDefault,
                IsVisibleByDefault = rows[index].IsVisibleByDefault
            };
        }

        return new UserWorksetsResult
        {
            Summary = summary,
            ReturnedCount = worksets.Length,
            Worksets = worksets
        };
    }
}
