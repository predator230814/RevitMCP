namespace BimIt.Revit.Addin.ReadOperations;

internal static class ApiAccessSpikeOperations
{
    public static void AddExperimental(List<IExecutableReadOperation> operations)
    {
        ArgumentNullException.ThrowIfNull(operations);
        operations.Add(new ExperimentalRevitApiLookupReadOperation());
        operations.Add(new ExperimentalRevitApiReadReadOperation());
    }
}
