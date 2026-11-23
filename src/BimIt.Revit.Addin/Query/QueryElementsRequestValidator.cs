using BimIt.Revit.Bridge;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Addin.Query;

internal static class QueryElementsRequestValidator
{
    public const int MinLimit = 1;
    public const int MaxLimit = 100;
    public const int MaxFilterValues = 20;
    public const int MaxFilterLength = 256;

    public static void Validate(QueryElementsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Scope is not (QueryScope.Document or QueryScope.ActiveView))
        {
            throw InvalidQuery();
        }

        if (request.Limit is < MinLimit or > MaxLimit)
        {
            throw InvalidQuery();
        }

        if (request.Filters is null)
        {
            throw InvalidQuery();
        }

        var hasPredicates = ValidatePredicates(request.Predicates);
        var documentNeedsCoarseFilter = request.Scope != QueryScope.ActiveView && !hasPredicates;
        ValidateFilters(request.Filters, documentNeedsCoarseFilter);
    }

    public static void ValidateFilters(QueryElementFilters filters)
    {
        ValidateFilters(filters, requireFilter: true);
    }

    public static void ValidateFilters(QueryElementFilters filters, bool requireFilter)
    {
        ArgumentNullException.ThrowIfNull(filters);

        var hasFilter = false;
        hasFilter |= ValidateArray(filters.CategoryNames);
        hasFilter |= ValidateArray(filters.FamilyNames);
        hasFilter |= ValidateArray(filters.TypeNames);
        hasFilter |= ValidateArray(filters.LevelNames);
        hasFilter |= ValidateText(filters.TextContains);

        if (!hasFilter && requireFilter)
        {
            throw InvalidQuery();
        }
    }

    private static bool ValidateArray(IReadOnlyList<string>? values)
    {
        if (values is null)
        {
            return false;
        }

        if (values.Count is < 1 or > MaxFilterValues)
        {
            throw InvalidQuery();
        }

        foreach (var value in values)
        {
            if (value is null || value.Length is < 1 or > MaxFilterLength)
            {
                throw InvalidQuery();
            }
        }

        return true;
    }

    private static bool ValidateText(string? value)
    {
        if (value is null)
        {
            return false;
        }

        if (value.Length is < 1 or > MaxFilterLength)
        {
            throw InvalidQuery();
        }

        return true;
    }

    private static bool ValidatePredicates(IReadOnlyList<QueryElementPredicate>? predicates)
    {
        if (predicates is null)
        {
            return false;
        }

        if (predicates.Count is < 1 or > QueryPredicateLimits.MaxPredicates)
        {
            throw InvalidQuery();
        }

        foreach (var predicate in predicates)
        {
            if (predicate is null
                || !string.Equals(predicate.Source, QueryPredicateRules.ParameterSource, StringComparison.Ordinal)
                || predicate.ParameterRef is null
                || predicate.ParameterRef.Length is < 1 or > QueryPredicateLimits.MaxParameterRefLength
                || !QueryPredicateRules.TryParseOperator(predicate.Operator, out var parsed)
                || !QueryPredicateRules.ValueMatches(parsed, predicate.Value)
                || predicate.Value is not null && !QueryPredicateRules.PayloadValid(predicate.Value))
            {
                throw InvalidQuery();
            }
        }

        return true;
    }

    private static BridgeException InvalidQuery()
    {
        return new BridgeException(
            CapabilityErrorCodes.InvalidQuery,
            "The query request is invalid.");
    }
}
