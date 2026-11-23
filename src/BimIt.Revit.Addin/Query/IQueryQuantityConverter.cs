namespace BimIt.Revit.Addin.Query;

internal interface IQueryQuantityConverter
{
    bool IsUnit(string unitTypeId);

    bool IsValidUnit(string specTypeId, string unitTypeId);

    double ConvertToInternalUnits(double value, string unitTypeId);
}
