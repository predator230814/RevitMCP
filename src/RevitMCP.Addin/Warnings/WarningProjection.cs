using RevitMCP.Addin.Inspection;
using RevitMCP.Contracts;

namespace RevitMCP.Addin.Warnings;

internal sealed class SourceWarning
{
    public required string FailureKey { get; init; }

    public required WarningSeverity Severity { get; init; }

    public required string Description { get; init; }

    public required bool HasResolutions { get; init; }

    public required IReadOnlyList<string> FailingRefs { get; init; }

    public required IReadOnlyList<string> AdditionalRefs { get; init; }

    public required int UnresolvedElementCount { get; init; }
}

internal static class WarningProjection
{
    public const int MaxDefinitionRows = 50;

    public static GetWarningsResult Project(
        string instanceId,
        string documentId,
        ValidatedGetWarningsRequest request,
        IReadOnlyList<SourceWarning> messages)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(messages);

        var afterKey = new List<SourceWarning>();
        foreach (var message in messages)
        {
            if (request.Severity is WarningSeverity severity && message.Severity != severity)
            {
                continue;
            }

            if (request.FailureKey is string failureKey
                && !string.Equals(message.FailureKey, failureKey, StringComparison.Ordinal))
            {
                continue;
            }

            afterKey.Add(message);
        }

        IReadOnlyList<string>? unmatched = null;
        if (request.ElementRefs is not null)
        {
            var cited = new HashSet<string>(StringComparer.Ordinal);
            foreach (var message in afterKey)
            {
                foreach (var elementRef in message.FailingRefs)
                {
                    cited.Add(elementRef);
                }

                foreach (var elementRef in message.AdditionalRefs)
                {
                    cited.Add(elementRef);
                }
            }

            var missing = new List<string>();
            foreach (var elementRef in request.ElementRefs)
            {
                if (!cited.Contains(elementRef))
                {
                    missing.Add(elementRef);
                }
            }

            unmatched = missing;
        }

        var filtered = request.ElementRefs is null
            ? afterKey
            : afterKey.Where(message => CitesAny(message, request.ElementRefs)).ToList();

        filtered.Sort(CompareMessages);

        var counts = Count(filtered);
        var definitions = BuildDefinitions(filtered, out var definitionsTruncated);
        var returned = filtered.Take(request.MaxWarnings).ToArray();
        var warningsTruncated = filtered.Count > returned.Length;
        var items = new GetWarningsItem[returned.Length];
        var elementsTruncated = false;
        for (var index = 0; index < returned.Length; index++)
        {
            items[index] = Shape(returned[index], request);
            elementsTruncated |= items[index].ElementsTruncated;
        }

        var reasons = new List<WarningTruncationReason>();
        if (definitionsTruncated)
        {
            reasons.Add(WarningTruncationReason.Definitions);
        }

        if (elementsTruncated)
        {
            reasons.Add(WarningTruncationReason.Elements);
        }

        if (warningsTruncated)
        {
            reasons.Add(WarningTruncationReason.Warnings);
        }

        reasons.Sort(static (left, right) => string.CompareOrdinal(WireReason(left), WireReason(right)));

        return new GetWarningsResult
        {
            Context = new GetWarningsContext
            {
                InstanceId = instanceId,
                DocumentId = documentId
            },
            MatchedCount = filtered.Count,
            CountsBySeverity = counts,
            Definitions = definitions,
            Warnings = items,
            UnmatchedElementRefs = unmatched,
            Truncated = reasons.Count > 0,
            TruncationReasons = reasons
        };
    }

    private static bool CitesAny(SourceWarning message, IReadOnlyList<string> elementRefs)
    {
        foreach (var elementRef in elementRefs)
        {
            if (message.FailingRefs.Contains(elementRef, StringComparer.Ordinal)
                || message.AdditionalRefs.Contains(elementRef, StringComparer.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static int CompareMessages(SourceWarning left, SourceWarning right)
    {
        var severity = Rank(left.Severity).CompareTo(Rank(right.Severity));
        if (severity != 0)
        {
            return severity;
        }

        var key = string.CompareOrdinal(left.FailureKey, right.FailureKey);
        if (key != 0)
        {
            return key;
        }

        var failing = CompareRefs(Sorted(left.FailingRefs), Sorted(right.FailingRefs));
        if (failing != 0)
        {
            return failing;
        }

        var additional = CompareRefs(Sorted(left.AdditionalRefs), Sorted(right.AdditionalRefs));
        if (additional != 0)
        {
            return additional;
        }

        return string.CompareOrdinal(left.Description, right.Description);
    }

    private static int Rank(WarningSeverity severity)
    {
        return severity switch
        {
            WarningSeverity.DocumentCorruption => 0,
            WarningSeverity.Error => 1,
            WarningSeverity.Warning => 2,
            _ => 3
        };
    }

    private static string[] Sorted(IReadOnlyList<string> refs)
    {
        var copy = refs.ToArray();
        Array.Sort(copy, StringComparer.Ordinal);
        return copy;
    }

    private static int CompareRefs(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        var count = Math.Min(left.Count, right.Count);
        for (var index = 0; index < count; index++)
        {
            var compare = string.CompareOrdinal(left[index], right[index]);
            if (compare != 0)
            {
                return compare;
            }
        }

        return left.Count.CompareTo(right.Count);
    }

    private static GetWarningsCounts Count(IReadOnlyList<SourceWarning> messages)
    {
        var warning = 0;
        var error = 0;
        var corruption = 0;
        var other = 0;
        foreach (var message in messages)
        {
            switch (message.Severity)
            {
                case WarningSeverity.Warning:
                    warning++;
                    break;
                case WarningSeverity.Error:
                    error++;
                    break;
                case WarningSeverity.DocumentCorruption:
                    corruption++;
                    break;
                default:
                    other++;
                    break;
            }
        }

        return new GetWarningsCounts
        {
            Warning = warning,
            Error = error,
            DocumentCorruption = corruption,
            Other = other
        };
    }

    private static GetWarningsDefinition[] BuildDefinitions(IReadOnlyList<SourceWarning> messages, out bool truncated)
    {
        var rows = messages
            .GroupBy(message => (message.FailureKey, message.Severity))
            .Select(group => new GetWarningsDefinition
            {
                FailureKey = group.Key.FailureKey,
                Severity = group.Key.Severity,
                MatchedCount = group.Count()
            })
            .OrderByDescending(row => row.MatchedCount)
            .ThenBy(row => row.FailureKey, StringComparer.Ordinal)
            .ThenBy(row => Rank(row.Severity))
            .ToArray();
        truncated = rows.Length > MaxDefinitionRows;
        return truncated ? rows[..MaxDefinitionRows] : rows;
    }

    private static GetWarningsItem Shape(SourceWarning message, ValidatedGetWarningsRequest request)
    {
        var bounded = ParameterValueTextBounder.Bound(message.Description);
        var description = bounded.Text ?? "";
        var admitted = Admit(message, request.ElementRefs, request.MaxElementsPerWarning);
        return new GetWarningsItem
        {
            FailureKey = message.FailureKey,
            Severity = message.Severity,
            DescriptionText = description,
            DescriptionTruncated = bounded.Truncated,
            HasResolutions = message.HasResolutions,
            Elements = admitted.Elements,
            ElementsTruncated = admitted.Truncated,
            UnresolvedElementCount = message.UnresolvedElementCount
        };
    }

    private static (IReadOnlyList<GetWarningsElement> Elements, bool Truncated) Admit(
        SourceWarning message,
        IReadOnlyList<string>? filter,
        int maxElements)
    {
        var failing = new HashSet<string>(message.FailingRefs, StringComparer.Ordinal);
        var ordered = new List<GetWarningsElement>();
        var emitted = new HashSet<string>(StringComparer.Ordinal);

        if (filter is not null)
        {
            foreach (var elementRef in filter)
            {
                if (!emitted.Add(elementRef))
                {
                    continue;
                }

                if (failing.Contains(elementRef))
                {
                    ordered.Add(Element(elementRef, WarningElementRole.Failing));
                }
                else if (message.AdditionalRefs.Contains(elementRef, StringComparer.Ordinal))
                {
                    ordered.Add(Element(elementRef, WarningElementRole.Additional));
                }
                else
                {
                    emitted.Remove(elementRef);
                }
            }
        }

        foreach (var elementRef in Sorted(message.FailingRefs))
        {
            if (emitted.Add(elementRef))
            {
                ordered.Add(Element(elementRef, WarningElementRole.Failing));
            }
        }

        foreach (var elementRef in Sorted(message.AdditionalRefs))
        {
            if (failing.Contains(elementRef) || !emitted.Add(elementRef))
            {
                continue;
            }

            ordered.Add(Element(elementRef, WarningElementRole.Additional));
        }

        if (ordered.Count <= maxElements)
        {
            return (ordered, false);
        }

        return (ordered.Take(maxElements).ToArray(), true);
    }

    private static GetWarningsElement Element(string elementRef, WarningElementRole role)
    {
        return new GetWarningsElement
        {
            ElementRef = elementRef,
            Role = role
        };
    }

    private static string WireReason(WarningTruncationReason reason)
    {
        return reason switch
        {
            WarningTruncationReason.Definitions => "definitions",
            WarningTruncationReason.Elements => "elements",
            WarningTruncationReason.Warnings => "warnings",
            _ => reason.ToString()
        };
    }
}
