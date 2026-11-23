using BimIt.Revit.Addin.Identity;
using BimIt.Revit.Bridge;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Addin.Query;

internal static class QueryPredicatePreparer
{
    public static IReadOnlyList<ResolvedParameterPredicate> Prepare(
        IReadOnlyList<QueryElementPredicate> predicates,
        Func<string, ParameterIdentityBinding?> resolve,
        IQueryQuantityConverter quantities)
    {
        ArgumentNullException.ThrowIfNull(predicates);
        ArgumentNullException.ThrowIfNull(resolve);
        ArgumentNullException.ThrowIfNull(quantities);

        var resolved = new List<ResolvedParameterPredicate>(predicates.Count);
        foreach (var predicate in predicates)
        {
            if (predicate.ParameterRef is null
                || !QueryPredicateRules.TryParseOperator(predicate.Operator, out var parsed)
                || !QueryPredicateRules.ValueMatches(parsed, predicate.Value)
                || predicate.Value is not null && !QueryPredicateRules.PayloadValid(predicate.Value))
            {
                throw InvalidQuery();
            }

            var binding = resolve(predicate.ParameterRef);
            if (binding is null)
            {
                throw InvalidQuery();
            }

            var prepared = new ResolvedParameterPredicate
            {
                Binding = binding.Value,
                Operator = parsed
            };

            switch (predicate.Value)
            {
                case null:
                    break;
                case QueryPredicateStringValue text:
                    prepared = Copy(prepared, text.Value, hasInteger: false, integer: 0, quantity: 0, unit: null);
                    break;
                case QueryPredicateIntegerValue integer:
                    prepared = Copy(prepared, null, hasInteger: true, integer.Value, quantity: 0, unit: null);
                    break;
                case QueryPredicateQuantityValue quantity:
                    if (!quantities.IsUnit(quantity.UnitTypeId))
                    {
                        throw InvalidQuery();
                    }

                    prepared = Copy(prepared, null, hasInteger: false, integer: 0, quantity.Value, quantity.UnitTypeId);
                    break;
                default:
                    throw InvalidQuery();
            }

            resolved.Add(prepared);
        }

        return resolved;
    }

    private static ResolvedParameterPredicate Copy(
        ResolvedParameterPredicate source,
        string? text,
        bool hasInteger,
        int integer,
        double quantity,
        string? unit)
    {
        return new ResolvedParameterPredicate
        {
            Binding = source.Binding,
            Operator = source.Operator,
            StringValue = text,
            HasInteger = hasInteger,
            IntegerValue = integer,
            QuantityValue = quantity,
            UnitTypeId = unit
        };
    }

    private static BridgeException InvalidQuery()
    {
        return new BridgeException(
            CapabilityErrorCodes.InvalidQuery,
            "The query request is invalid.");
    }
}
