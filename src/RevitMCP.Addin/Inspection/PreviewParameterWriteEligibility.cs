namespace RevitMCP.Addin.Inspection;

internal static class PreviewParameterWriteEligibility
{
    internal static bool IsWritable(bool isReadOnly, bool isShared, bool userModifiable)
        => !isReadOnly && (!isShared || userModifiable);
}
