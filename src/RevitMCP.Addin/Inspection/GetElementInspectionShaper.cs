using RevitMCP.Contracts;

namespace RevitMCP.Addin.Inspection;

internal static class GetElementInspectionShaper
{
    public const int MaxParameterEntries = 20;

    public static GetElementsResult Shape(
        GetElementsContext context,
        IReadOnlyList<string> elementRefs,
        IReadOnlyDictionary<string, ElementInspectionCandidate> resolved,
        GetElementsProjection projection)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(elementRefs);
        ArgumentNullException.ThrowIfNull(resolved);
        ArgumentNullException.ThrowIfNull(projection);

        var elements = new GetElementResult[elementRefs.Count];
        for (var index = 0; index < elementRefs.Count; index++)
        {
            var elementRef = elementRefs[index];
            elements[index] = resolved.TryGetValue(elementRef, out var candidate)
                ? Ok(candidate, projection)
                : NotFound(elementRef);
        }

        return new GetElementsResult
        {
            Context = context,
            Elements = elements
        };
    }

    public static GetElementResult Ok(ElementInspectionCandidate candidate, GetElementsProjection projection)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(projection);

        IReadOnlyList<GetElementParameter>? parameters = null;
        bool? parametersTruncated = null;
        if (projection.ParameterNames is not null)
        {
            var projected = ProjectParameters(
                projection.ParameterNames,
                candidate.InstanceParameters,
                candidate.TypeParameters);
            parameters = projected.Parameters;
            parametersTruncated = projected.Truncated;
        }

        return new GetElementResult
        {
            ElementRef = candidate.ElementRef,
            Status = GetElementResultStatus.Ok,
            Name = ProjectField(projection.Fields, GetElementField.Name, candidate.Name),
            CategoryName = ProjectField(projection.Fields, GetElementField.CategoryName, candidate.CategoryName),
            FamilyName = ProjectField(projection.Fields, GetElementField.FamilyName, candidate.FamilyName),
            TypeName = ProjectField(projection.Fields, GetElementField.TypeName, candidate.TypeName),
            LevelName = ProjectField(projection.Fields, GetElementField.LevelName, candidate.LevelName),
            Parameters = parameters,
            ParametersTruncated = parametersTruncated
        };
    }

    public static GetElementResult NotFound(string elementRef)
    {
        ArgumentNullException.ThrowIfNull(elementRef);
        return new GetElementResult
        {
            ElementRef = elementRef,
            Status = GetElementResultStatus.NotFound
        };
    }

    internal static (IReadOnlyList<GetElementParameter> Parameters, bool Truncated) ProjectParameters(
        IReadOnlyList<string> requestedNames,
        IReadOnlyList<ParameterInspectionCandidate> instanceParameters,
        IReadOnlyList<ParameterInspectionCandidate> typeParameters)
    {
        ArgumentNullException.ThrowIfNull(requestedNames);
        ArgumentNullException.ThrowIfNull(instanceParameters);
        ArgumentNullException.ThrowIfNull(typeParameters);

        var matches = new List<GetElementParameter>();
        foreach (var requestedName in requestedNames)
        {
            AppendMatches(matches, requestedName, instanceParameters, GetElementParameterSource.Instance);
            AppendMatches(matches, requestedName, typeParameters, GetElementParameterSource.Type);
        }

        var truncated = matches.Count > MaxParameterEntries;
        if (truncated)
        {
            matches.RemoveRange(MaxParameterEntries, matches.Count - MaxParameterEntries);
        }

        return (matches, truncated);
    }

    private static void AppendMatches(
        List<GetElementParameter> matches,
        string requestedName,
        IReadOnlyList<ParameterInspectionCandidate> candidates,
        GetElementParameterSource source)
    {
        foreach (var candidate in candidates)
        {
            if (!string.Equals(candidate.Name, requestedName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var (text, truncated) = ParameterValueTextBounder.Bound(candidate.ValueText);
            matches.Add(new GetElementParameter
            {
                Name = candidate.Name,
                Source = source,
                ValueText = text,
                ValueTruncated = truncated
            });
        }
    }

    private static ProjectedString ProjectField(
        IReadOnlyList<GetElementField>? fields,
        GetElementField field,
        string? value)
    {
        if (fields is null || !Contains(fields, field))
        {
            return ProjectedString.Omitted;
        }

        return ProjectedString.FromRequested(value);
    }

    private static bool Contains(IReadOnlyList<GetElementField> fields, GetElementField field)
    {
        foreach (var candidate in fields)
        {
            if (candidate == field)
            {
                return true;
            }
        }

        return false;
    }
}
