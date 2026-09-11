namespace RevitMCP.Server;

internal static class ToolErrorMessages
{
    public const string NoRevitInstance = "No eligible Revit instance is available.";
    public const string InstanceRequired = "Multiple eligible Revit instances are available. Specify instance_id.";
    public const string InstanceNotFound = "The specified Revit instance was not found.";
    public const string InstanceUnavailable = "The selected Revit instance is not available.";
    public const string ExecutionTimeout = "The Revit context request timed out.";
    public const string ExecutionFailed = "The Revit context could not be collected.";
    public const string InvalidRequest = "The tool request contains unexpected properties.";
}
