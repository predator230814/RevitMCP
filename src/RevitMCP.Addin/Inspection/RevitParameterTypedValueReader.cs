using Autodesk.Revit.DB;
using RevitMCP.Contracts;

namespace RevitMCP.Addin.Inspection;

internal static class RevitParameterTypedValueReader
{
    public static bool TryRead(Parameter parameter, Document document, out GetParameterValue? value)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        ArgumentNullException.ThrowIfNull(document);

        value = null;
        if (!parameter.HasValue)
        {
            return true;
        }

        switch (parameter.StorageType)
        {
            case StorageType.String:
                var stored = parameter.AsString();
                if (stored is null)
                {
                    return false;
                }

                var bounded = ParameterValueTextBounder.Bound(stored);
                value = new GetParameterStringValue
                {
                    Value = bounded.Text!,
                    Truncated = bounded.Truncated
                };
                return true;

            case StorageType.Integer:
                value = new GetParameterIntegerValue
                {
                    Value = parameter.AsInteger()
                };
                return true;

            case StorageType.Double:
                return TryReadQuantity(parameter, out value);

            case StorageType.ElementId:
                value = ReadElementReference(parameter, document);
                return true;

            default:
                return false;
        }
    }

    private static bool TryReadQuantity(Parameter parameter, out GetParameterValue? value)
    {
        value = null;
        var spec = parameter.Definition?.GetDataType();
        if (spec is null || spec.Empty() || !UnitUtils.IsMeasurableSpec(spec))
        {
            return false;
        }

        ForgeTypeId unit;
        try
        {
            unit = parameter.GetUnitTypeId();
        }
        catch (InvalidOperationException)
        {
            return false;
        }

        if (unit is null || unit.Empty() || !UnitUtils.IsUnit(unit) || !UnitUtils.IsValidUnit(spec, unit))
        {
            return false;
        }

        double converted;
        try
        {
            converted = UnitUtils.ConvertFromInternalUnits(parameter.AsDouble(), unit);
        }
        catch (ArgumentException)
        {
            return false;
        }

        if (!double.IsFinite(converted))
        {
            return false;
        }

        value = new GetParameterQuantityValue
        {
            Value = converted,
            UnitTypeId = unit.TypeId
        };
        return true;
    }

    private static GetParameterElementReferenceValue ReadElementReference(Parameter parameter, Document document)
    {
        var id = parameter.AsElementId();
        if (id is null || id == ElementId.InvalidElementId)
        {
            return new GetParameterElementReferenceValue
            {
                Resolved = false
            };
        }

        var target = document.GetElement(id);
        if (target is null)
        {
            return new GetParameterElementReferenceValue
            {
                Resolved = false
            };
        }

        var name = ParameterValueTextBounder.Bound(target.Name).Text;
        if (target is ElementType)
        {
            return new GetParameterElementReferenceValue
            {
                Resolved = true,
                Name = name
            };
        }

        return new GetParameterElementReferenceValue
        {
            Resolved = true,
            Name = name,
            ElementRef = string.IsNullOrEmpty(target.UniqueId) ? null : target.UniqueId
        };
    }
}
