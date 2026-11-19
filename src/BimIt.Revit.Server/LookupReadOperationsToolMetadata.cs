namespace BimIt.Revit.Server;

internal static class LookupReadOperationsToolMetadata
{
    public const string Name = "revit_lookup";

    public const string Title = "Lookup Revit read operations";

    public const string Description =
        "Lists or describes BIM-IT registered read operations on the connected Revit host. It does not describe the Revit API. Omit operation to search, with an optional query, offset, and limit; search returns summaries and no schemas. Pass operation to get the full descriptor, including input and output schemas; contract_version is optional in that mode. A reachable Revit host is required, and an active document is not.";
}
