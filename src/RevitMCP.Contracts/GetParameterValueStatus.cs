namespace RevitMCP.Contracts;

public enum GetParameterValueStatus
{
    Ok,
    ElementNotFound,
    ParameterRefNotFound,
    ParameterNotPresent,
    UnsupportedValue
}
