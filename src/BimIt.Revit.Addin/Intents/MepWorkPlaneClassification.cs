using BimIt.Revit.Contracts;

namespace BimIt.Revit.Addin.Intents;

/// <summary>
/// Closed decision for FamilyPlacementType.WorkPlaneBased.
/// HostFace present means a geometry face. Level equality is a separate Revit API fact:
/// SketchPlane.GetPlaneReference().EqualTo(Level.GetPlaneReference()).
/// </summary>
internal static class MepWorkPlaneClassification
{
    public static PreviewMepDevicePlacementMode Classify(bool hostFacePresent, bool levelPlaneReferenceEqual)
    {
        if (hostFacePresent)
        {
            return PreviewMepDevicePlacementMode.WorkPlaneBasedFace;
        }

        return levelPlaneReferenceEqual
            ? PreviewMepDevicePlacementMode.WorkPlaneBasedLevel
            : PreviewMepDevicePlacementMode.WorkPlaneBasedOther;
    }
}
