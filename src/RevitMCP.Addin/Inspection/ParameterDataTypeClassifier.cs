using RevitMCP.Contracts;

namespace RevitMCP.Addin.Inspection;

internal static class ParameterDataTypeClassifier
{
    public static DescribeParameterDataType Classify(
        string? forgeTypeId,
        bool isMeasurableSpec,
        bool isBuiltInCategory,
        bool isSpec)
    {
        if (string.IsNullOrEmpty(forgeTypeId))
        {
            return new DescribeParameterDataType
            {
                Kind = DescribeParameterDataTypeKind.Unknown
            };
        }

        if (isMeasurableSpec)
        {
            return new DescribeParameterDataType
            {
                Kind = DescribeParameterDataTypeKind.MeasurableSpec,
                ForgeTypeId = forgeTypeId
            };
        }

        if (isBuiltInCategory)
        {
            return new DescribeParameterDataType
            {
                Kind = DescribeParameterDataTypeKind.Category,
                ForgeTypeId = forgeTypeId
            };
        }

        if (isSpec)
        {
            return new DescribeParameterDataType
            {
                Kind = DescribeParameterDataTypeKind.Spec,
                ForgeTypeId = forgeTypeId
            };
        }

        return new DescribeParameterDataType
        {
            Kind = DescribeParameterDataTypeKind.Unknown,
            ForgeTypeId = forgeTypeId
        };
    }
}
