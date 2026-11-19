using Autodesk.Revit.DB;
using BimIt.Revit.Bridge;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Addin.ReadOperations;

internal static class DocumentModificationGuard
{
    public static T Read<T>(Document document, Func<T> read)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(read);
        var before = document.IsModified;
        T result;
        try
        {
            result = read();
        }
        catch (BridgeException)
        {
            EnsureUnchanged(document, before);
            throw;
        }
        catch (Exception)
        {
            EnsureUnchanged(document, before);
            throw new BridgeException(CapabilityErrorCodes.ExecutionFailed, "The read operation could not be read.");
        }

        EnsureUnchanged(document, before);
        return result;
    }

    private static void EnsureUnchanged(Document document, bool before)
    {
        if (document.IsModified != before)
        {
            throw new BridgeException(
                CapabilityErrorCodes.ExecutionFailed,
                "The read changed Document.IsModified.");
        }
    }
}
