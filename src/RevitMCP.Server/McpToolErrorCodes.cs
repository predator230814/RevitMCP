namespace RevitMCP.Server;

internal static class McpToolErrorCodes
{
    public const string NoRevitInstance = "NO_REVIT_INSTANCE";
    public const string InstanceRequired = "INSTANCE_REQUIRED";
    public const string InstanceNotFound = "INSTANCE_NOT_FOUND";
    public const string InstanceUnavailable = "INSTANCE_UNAVAILABLE";
    public const string ExecutionTimeout = "REVIT_EXECUTION_TIMEOUT";
    public const string ExecutionFailed = "REVIT_EXECUTION_FAILED";
}
