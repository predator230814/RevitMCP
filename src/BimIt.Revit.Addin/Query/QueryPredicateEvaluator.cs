using BimIt.Revit.Addin.Identity;
using BimIt.Revit.Bridge;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Addin.Query;

internal sealed class QueryPredicateEvaluator
{
    private readonly IQueryQuantityConverter _quantities;

    public QueryPredicateEvaluator(IQueryQuantityConverter quantities)
    {
        ArgumentNullException.ThrowIfNull(quantities);
        _quantities = quantities;
    }

    public bool MatchesAll(
        IReadOnlyList<ResolvedParameterPredicate> predicates,
        Func<ParameterIdentityBinding, QueryParameterSurface> resolve)
    {
        ArgumentNullException.ThrowIfNull(predicates);
        ArgumentNullException.ThrowIfNull(resolve);

        var matched = true;
        var seen = new Dictionary<ParameterIdentityBinding, QueryParameterSurface>();
        foreach (var predicate in predicates)
        {
            if (!seen.TryGetValue(predicate.Binding, out var surface))
            {
                surface = resolve(predicate.Binding);
                seen.Add(predicate.Binding, surface);
            }

            if (!Matches(predicate, surface))
            {
                matched = false;
            }
        }

        return matched;
    }

    public bool Matches(ResolvedParameterPredicate predicate, QueryParameterSurface surface)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(surface);

        switch (predicate.Operator)
        {
            case QueryPredicateOperator.IsMissing:
                return !surface.Present;
            case QueryPredicateOperator.IsPresent:
                return surface.Present;
            case QueryPredicateOperator.IsEmpty:
                return surface.Present && IsEmpty(surface);
            case QueryPredicateOperator.IsNotEmpty:
                return surface.Present && !IsEmpty(surface);
            case QueryPredicateOperator.Equals:
            case QueryPredicateOperator.NotEquals:
            case QueryPredicateOperator.Contains:
            case QueryPredicateOperator.GreaterThan:
            case QueryPredicateOperator.GreaterOrEqual:
            case QueryPredicateOperator.LessThan:
            case QueryPredicateOperator.LessOrEqual:
                return Compare(predicate, surface);
            default:
                throw InvalidQuery();
        }
    }

    private bool Compare(ResolvedParameterPredicate predicate, QueryParameterSurface surface)
    {
        if (!surface.Present || !surface.HasValue)
        {
            return false;
        }

        if (predicate.HasString)
        {
            if (surface.Storage != QueryParameterStorage.String)
            {
                return false;
            }

            var actual = surface.StringValue ?? string.Empty;
            var expected = predicate.StringValue ?? string.Empty;
            var equal = string.Equals(actual, expected, StringComparison.Ordinal);
            return predicate.Operator switch
            {
                QueryPredicateOperator.Equals => equal,
                QueryPredicateOperator.NotEquals => !equal,
                QueryPredicateOperator.Contains => actual.Contains(expected, StringComparison.Ordinal),
                _ => false
            };
        }

        if (predicate.HasInteger)
        {
            if (surface.Storage != QueryParameterStorage.Integer)
            {
                return false;
            }

            return CompareNumber(surface.IntegerValue, predicate.IntegerValue, predicate.Operator);
        }

        if (predicate.HasQuantity)
        {
            if (surface.Storage != QueryParameterStorage.Double)
            {
                return false;
            }

            var expected = ConvertQuantity(surface, predicate);
            return CompareNumber(surface.DoubleValue, expected, predicate.Operator);
        }

        return false;
    }

    private double ConvertQuantity(QueryParameterSurface surface, ResolvedParameterPredicate predicate)
    {
        if (string.IsNullOrEmpty(surface.SpecTypeId) || !surface.MeasurableSpec)
        {
            throw InvalidQuery();
        }

        var unit = predicate.UnitTypeId!;
        if (!_quantities.IsValidUnit(surface.SpecTypeId, unit))
        {
            throw InvalidQuery();
        }

        var converted = _quantities.ConvertToInternalUnits(predicate.QuantityValue, unit);
        if (!double.IsFinite(converted))
        {
            throw InvalidQuery();
        }

        return converted;
    }

    private static bool CompareNumber(double actual, double expected, QueryPredicateOperator parsed)
    {
        return parsed switch
        {
            QueryPredicateOperator.Equals => actual == expected,
            QueryPredicateOperator.NotEquals => actual != expected,
            QueryPredicateOperator.GreaterThan => actual > expected,
            QueryPredicateOperator.GreaterOrEqual => actual >= expected,
            QueryPredicateOperator.LessThan => actual < expected,
            QueryPredicateOperator.LessOrEqual => actual <= expected,
            _ => false
        };
    }

    private static bool IsEmpty(QueryParameterSurface surface)
    {
        if (!surface.HasValue)
        {
            return true;
        }

        if (surface.Storage == QueryParameterStorage.String)
        {
            return string.IsNullOrEmpty(surface.StringValue);
        }

        return false;
    }

    private static BridgeException InvalidQuery()
    {
        return new BridgeException(
            CapabilityErrorCodes.InvalidQuery,
            "The query request is invalid.");
    }
}
