namespace RevitMCP.Server;

internal static class DescribeParametersToolMetadata
{
    public const string Name = "revit_describe_parameters";
    public const string Title = "Describe Revit Parameters";
    public const string Description =
        "Discover visible parameter definitions on a bounded set of known Revit element references and return opaque parameter identity plus data-type semantics, without parameter values.";

    public const int DefaultLimit = 50;
}
