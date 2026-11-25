namespace BimIt.Revit.Addin.CSharpQuery;

internal readonly record struct CSharpQueryDocumentObservation(
    bool HasActiveDocument,
    bool IsFamilyDocument,
    bool IdMatches,
    bool IsModifiable,
    bool ApiMatches)
{
    public static CSharpQueryDocumentObservation None { get; } = new(false, false, false, false, false);
}

internal readonly record struct CSharpQueryGateDecision(string? Code, string? Reason)
{
    public bool Accepted => Code is null;
}

internal static class CSharpQueryDocumentGate
{
    public static CSharpQueryGateDecision Decide(CSharpQueryDocumentObservation observation)
    {
        if (!observation.HasActiveDocument)
        {
            return new CSharpQueryGateDecision(CSharpQueryCodes.DocumentNotReady, CSharpQueryCodes.NoActiveDocument);
        }

        if (observation.IsFamilyDocument)
        {
            return new CSharpQueryGateDecision(CSharpQueryCodes.DocumentNotReady, CSharpQueryCodes.ProjectDocumentRequired);
        }

        if (!observation.IdMatches)
        {
            return new CSharpQueryGateDecision(CSharpQueryCodes.DocumentNotReady, CSharpQueryCodes.DocumentMismatch);
        }

        if (observation.IsModifiable)
        {
            return new CSharpQueryGateDecision(CSharpQueryCodes.DocumentNotReady, CSharpQueryCodes.DocumentModifiable);
        }

        if (!observation.ApiMatches)
        {
            return new CSharpQueryGateDecision(CSharpQueryCodes.ApiUnsupported, null);
        }

        return new CSharpQueryGateDecision(null, null);
    }
}
