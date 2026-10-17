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
    public ControlledWriteIntent(ControlledWriteIntentBinding binding, IntentEntry? parameterUpdates, MepDevicePlacementIntent? mepDevicePlacements)
    {
        Binding = binding;
        ParameterUpdates = parameterUpdates;
        MepDevicePlacements = mepDevicePlacements;
    }

    public ControlledWriteIntentBinding Binding { get; }

    public IntentEntry? ParameterUpdates { get; }

    public MepDevicePlacementIntent? MepDevicePlacements { get; }
}
