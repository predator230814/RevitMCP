namespace BimIt.Revit.Contracts;

public enum PreviewParameterUpdateStatus
{
    Ok,
    ElementNotFound,
    ParameterRefNotFound,
    ParameterNotPresent,
    UnsupportedParameterSource,
    ParameterNotWritable,
    ValueTypeMismatch,
    InvalidUnit,
    UnsupportedValue,
    NoChange
}
