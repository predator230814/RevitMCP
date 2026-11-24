using Autodesk.Revit.DB;
using BimIt.Revit.Bridge;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Addin.Query;

internal sealed class RevitQueryQuantityConverter : IQueryQuantityConverter
{
    public bool IsUnit(string unitTypeId)
    {
        if (!TryCreate(unitTypeId, out var unit))
        {
            return false;
        }

        try
        {
            return UnitUtils.IsUnit(unit);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public bool IsValidUnit(string specTypeId, string unitTypeId)
    {
        if (!TryCreate(specTypeId, out var spec) || !TryCreate(unitTypeId, out var unit))
        {
            return false;
        }

        try
        {
            return UnitUtils.IsValidUnit(spec, unit);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public double ConvertToInternalUnits(double value, string unitTypeId)
    {
        if (!TryCreate(unitTypeId, out var unit))
        {
            throw InvalidQuery();
        }

        try
        {
            return UnitUtils.ConvertToInternalUnits(value, unit);
        }
        catch (ArgumentException)
        {
            throw InvalidQuery();
        }
    }

    private static bool TryCreate(string typeId, out ForgeTypeId parsed)
    {
        try
        {
            parsed = new ForgeTypeId(typeId);
        }
        catch (ArgumentException)
        {
            parsed = null!;
            return false;
        }

        return parsed is not null && !parsed.Empty();
    }

    private static BridgeException InvalidQuery()
    {
        return new BridgeException(
            CapabilityErrorCodes.InvalidQuery,
            "The query request is invalid.");
    }
}
