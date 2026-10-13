namespace BimIt.Revit.Contracts;

public enum GetParameterValueStatus
{
    Ok,
    ElementNotFound,
    ParameterRefNotFound,
    ParameterNotPresent,
    UnsupportedValue
}
