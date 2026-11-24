using BimIt.Revit.Addin.Identity;

namespace BimIt.Revit.Addin.Query;

internal sealed class ResolvedParameterPredicate
{
    public required ParameterIdentityBinding Binding { get; init; }

    public required QueryPredicateOperator Operator { get; init; }

    public string? StringValue { get; init; }

    public int IntegerValue { get; init; }

    public double QuantityValue { get; init; }

    public string? UnitTypeId { get; init; }

    public bool HasString => StringValue is not null;

    public bool HasInteger { get; init; }

    public bool HasQuantity => UnitTypeId is not null;
}
