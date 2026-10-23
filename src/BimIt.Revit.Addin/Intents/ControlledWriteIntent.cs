namespace BimIt.Revit.Addin.Intents;

internal sealed record ControlledWriteIntentBinding(
    string IntentRef,
    string IntentFingerprint,
    ControlledWriteIntentKind Kind,
    int FingerprintSchemaVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    string InstanceId,
    string DocumentId,
    int ItemCount);

internal sealed class ControlledWriteIntent
{
    public ControlledWriteIntent(
        ControlledWriteIntentBinding binding,
        IntentEntry? parameterUpdates,
        MepDevicePlacementIntent? mepDevicePlacements,
        PatternMepPlacementIntent? patternMepPlacements = null)
    {
        Binding = binding;
        ParameterUpdates = parameterUpdates;
        MepDevicePlacements = mepDevicePlacements;
        PatternMepPlacements = patternMepPlacements;
    }

    public ControlledWriteIntentBinding Binding { get; }

    public IntentEntry? ParameterUpdates { get; }

    public MepDevicePlacementIntent? MepDevicePlacements { get; }

    public PatternMepPlacementIntent? PatternMepPlacements { get; }
}
