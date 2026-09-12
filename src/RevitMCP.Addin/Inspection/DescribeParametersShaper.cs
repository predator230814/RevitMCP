using RevitMCP.Contracts;

namespace RevitMCP.Addin.Inspection;

internal static class DescribeParametersShaper
{
    public static DescribeParametersResult Shape(
        DescribeParametersContext context,
        IReadOnlyList<string> elementRefs,
        IReadOnlySet<string> resolvedRefs,
        IReadOnlyList<DescribeParameterOccurrence> occurrences,
        string? nameContains,
        int limit)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(elementRefs);
        ArgumentNullException.ThrowIfNull(resolvedRefs);
        ArgumentNullException.ThrowIfNull(occurrences);

        var elements = new DescribeParameterElementResult[elementRefs.Count];
        for (var index = 0; index < elementRefs.Count; index++)
        {
            var elementRef = elementRefs[index];
            elements[index] = new DescribeParameterElementResult
            {
                ElementRef = elementRef,
                Status = resolvedRefs.Contains(elementRef)
                    ? GetElementResultStatus.Ok
                    : GetElementResultStatus.NotFound
            };
        }

        var aggregated = Aggregate(occurrences);
        if (nameContains is not null)
        {
            aggregated = aggregated
                .Where(descriptor => descriptor.Name.Contains(nameContains, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        aggregated.Sort(CompareDescriptors);
        var matchedCount = aggregated.Count;
        if (aggregated.Count > limit)
        {
            aggregated.RemoveRange(limit, aggregated.Count - limit);
        }

        return new DescribeParametersResult
        {
            Context = context,
            Elements = elements,
            MatchedCount = matchedCount,
            Truncated = matchedCount > aggregated.Count,
            Parameters = aggregated
        };
    }

    internal static List<DescribeParameterDescriptor> Aggregate(IReadOnlyList<DescribeParameterOccurrence> occurrences)
    {
        var groups = new Dictionary<string, DescriptorAccumulator>(StringComparer.Ordinal);
        foreach (var occurrence in occurrences)
        {
            if (!groups.TryGetValue(occurrence.ParameterRef, out var accumulator))
            {
                accumulator = new DescriptorAccumulator(occurrence);
                groups.Add(occurrence.ParameterRef, accumulator);
            }

            accumulator.Add(occurrence);
        }

        return groups.Values.Select(accumulator => accumulator.ToDescriptor()).ToList();
    }

    internal static int CompareDescriptors(DescribeParameterDescriptor left, DescribeParameterDescriptor right)
    {
        var ignoreCase = string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
        if (ignoreCase != 0)
        {
            return ignoreCase;
        }

        var ordinal = string.Compare(left.Name, right.Name, StringComparison.Ordinal);
        if (ordinal != 0)
        {
            return ordinal;
        }

        var source = CompareSource(left.Source).CompareTo(CompareSource(right.Source));
        if (source != 0)
        {
            return source;
        }

        return string.Compare(left.ParameterRef, right.ParameterRef, StringComparison.Ordinal);
    }

    private static int CompareSource(GetElementParameterSource source) =>
        source == GetElementParameterSource.Instance ? 0 : 1;

    private sealed class DescriptorAccumulator
    {
        private readonly DescribeParameterOccurrence _sample;
        private readonly HashSet<string> _present = new(StringComparer.Ordinal);
        private readonly HashSet<string> _readOnly = new(StringComparer.Ordinal);

        public DescriptorAccumulator(DescribeParameterOccurrence sample)
        {
            _sample = sample;
        }

        public void Add(DescribeParameterOccurrence occurrence)
        {
            _present.Add(occurrence.ElementRef);
            if (occurrence.IsReadOnly)
            {
                _readOnly.Add(occurrence.ElementRef);
            }
        }

        public DescribeParameterDescriptor ToDescriptor()
        {
            return new DescribeParameterDescriptor
            {
                ParameterRef = _sample.ParameterRef,
                Name = _sample.Name,
                Source = _sample.Source,
                Identity = _sample.Identity,
                DataType = _sample.DataType,
                PresentOnCount = _present.Count,
                ReadOnlyOnCount = _readOnly.Count
            };
        }
    }
}
