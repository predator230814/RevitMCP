using BimIt.Revit.Contracts;

namespace BimIt.Revit.Addin.Query;

internal enum QueryPredicateOperator
{
    IsMissing,
    IsPresent,
    IsEmpty,
    IsNotEmpty,
    Equals,
    NotEquals,
    Contains,
    GreaterThan,
    GreaterOrEqual,
    LessThan,
    LessOrEqual
}

internal static class QueryPredicateRules
{
    public const string ParameterSource = "parameter";

    public static bool TryParseOperator(string? text, out QueryPredicateOperator parsed)
    {
        switch (text)
        {
            case "is_missing":
                parsed = QueryPredicateOperator.IsMissing;
                return true;
            case "is_present":
                parsed = QueryPredicateOperator.IsPresent;
                return true;
            case "is_empty":
                parsed = QueryPredicateOperator.IsEmpty;
                return true;
            case "is_not_empty":
                parsed = QueryPredicateOperator.IsNotEmpty;
                return true;
            case "equals":
                parsed = QueryPredicateOperator.Equals;
                return true;
            case "not_equals":
                parsed = QueryPredicateOperator.NotEquals;
                return true;
            case "contains":
                parsed = QueryPredicateOperator.Contains;
                return true;
            case "greater_than":
                parsed = QueryPredicateOperator.GreaterThan;
                return true;
            case "greater_or_equal":
                parsed = QueryPredicateOperator.GreaterOrEqual;
                return true;
            case "less_than":
                parsed = QueryPredicateOperator.LessThan;
                return true;
            case "less_or_equal":
                parsed = QueryPredicateOperator.LessOrEqual;
                return true;
            default:
                parsed = default;
                return false;
        }
    }

    public static bool IsExistence(QueryPredicateOperator parsed)
    {
        return parsed is QueryPredicateOperator.IsMissing
            or QueryPredicateOperator.IsPresent
            or QueryPredicateOperator.IsEmpty
            or QueryPredicateOperator.IsNotEmpty;
    }

    public static bool ValueMatches(QueryPredicateOperator parsed, QueryPredicateValue? value)
    {
        if (IsExistence(parsed))
        {
            return value is null;
        }

        return parsed switch
        {
            QueryPredicateOperator.Contains => value is QueryPredicateStringValue,
            QueryPredicateOperator.Equals or QueryPredicateOperator.NotEquals =>
                value is QueryPredicateStringValue or QueryPredicateIntegerValue,
            QueryPredicateOperator.GreaterThan
                or QueryPredicateOperator.GreaterOrEqual
                or QueryPredicateOperator.LessThan
                or QueryPredicateOperator.LessOrEqual =>
                value is QueryPredicateIntegerValue or QueryPredicateQuantityValue,
            _ => false
        };
    }

    public static bool PayloadValid(QueryPredicateValue value)
    {
        return value switch
        {
            QueryPredicateStringValue text =>
                text.Value is not null && text.Value.Length <= QueryPredicateLimits.MaxStringLength,
            QueryPredicateIntegerValue => true,
            QueryPredicateQuantityValue quantity =>
                double.IsFinite(quantity.Value)
                && quantity.UnitTypeId is not null
                && quantity.UnitTypeId.Length is >= 1 and <= QueryPredicateLimits.MaxUnitTypeIdLength,
            _ => false
        };
    }
}
