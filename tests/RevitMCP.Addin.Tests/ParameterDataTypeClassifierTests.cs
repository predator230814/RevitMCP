using RevitMCP.Addin.Inspection;
using RevitMCP.Contracts;
using Xunit;

namespace RevitMCP.Addin.Tests;

public sealed class ParameterDataTypeClassifierTests
{
    [Fact]
    public void Empty_data_type_is_unknown_without_forge_type_id()
    {
        var dataType = ParameterDataTypeClassifier.Classify(null, false, false, false);
        Assert.Equal(DescribeParameterDataTypeKind.Unknown, dataType.Kind);
        Assert.Null(dataType.ForgeTypeId);
    }

    [Fact]
    public void Measurable_spec_wins_before_other_classifiers()
    {
        var dataType = ParameterDataTypeClassifier.Classify("autodesk.spec.aec:airflow", true, true, true);
        Assert.Equal(DescribeParameterDataTypeKind.MeasurableSpec, dataType.Kind);
        Assert.Equal("autodesk.spec.aec:airflow", dataType.ForgeTypeId);
    }

    [Fact]
    public void Built_in_category_is_classified_as_category()
    {
        var dataType = ParameterDataTypeClassifier.Classify("autodesk.revit.category:ost-generic-model", false, true, false);
        Assert.Equal(DescribeParameterDataTypeKind.Category, dataType.Kind);
    }

    [Fact]
    public void Spec_is_classified_after_category()
    {
        var dataType = ParameterDataTypeClassifier.Classify("autodesk.spec.aec:number", false, false, true);
        Assert.Equal(DescribeParameterDataTypeKind.Spec, dataType.Kind);
    }

    [Fact]
    public void Unrecognized_non_empty_type_is_unknown_but_keeps_forge_type_id()
    {
        var dataType = ParameterDataTypeClassifier.Classify("autodesk.revit.special:other", false, false, false);
        Assert.Equal(DescribeParameterDataTypeKind.Unknown, dataType.Kind);
        Assert.Equal("autodesk.revit.special:other", dataType.ForgeTypeId);
    }
}
