using Autodesk.Revit.DB;

namespace BimIt.Revit.Addin.Query;

internal sealed class RevitQueryQuantityConverter : IQueryQuantityConverter
{
    public bool IsUnit(string unitTypeId)
    {
        return UnitUtils.IsUnit(new ForgeTypeId(unitTypeId));
    }

    public bool IsValidUnit(string specTypeId, string unitTypeId)
    {
        return UnitUtils.IsValidUnit(new ForgeTypeId(specTypeId), new ForgeTypeId(unitTypeId));
    }

    public double ConvertToInternalUnits(double value, string unitTypeId)
    {
        return UnitUtils.ConvertToInternalUnits(value, new ForgeTypeId(unitTypeId));
    }
}
