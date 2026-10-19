namespace BimIt.Revit.Contracts;

public enum PreviewMepDeviceTemplateStatus
{
    Ok,
    NotFound,
    NotFamilyInstance,
    UnsupportedCategory,
    UnsupportedPlacement,
    UnsupportedHost,
    UnsupportedHostGeometry,
    UnsupportedLocation,
    UnsupportedLevel
}
