using RevitMCP.Contracts;

namespace RevitMCP.Addin.Inspection;

internal sealed class DescribeParameterOccurrence
{
    public required string ParameterRef { get; init; }

    public required string Name { get; init; }

    public required GetElementParameterSource Source { get; init; }

    public required DescribeParameterIdentity Identity { get; init; }

    public required DescribeParameterDataType DataType { get; init; }

    public required string ElementRef { get; init; }

    public required bool IsReadOnly { get; init; }
}
