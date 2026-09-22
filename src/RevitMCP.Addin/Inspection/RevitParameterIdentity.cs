using System.Globalization;
using Autodesk.Revit.DB;
using RevitMCP.Contracts;

namespace RevitMCP.Addin.Inspection;

internal static class RevitParameterIdentity
{
    public static ClassifiedParameterIdentity Classify(Parameter parameter, Document document)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        ArgumentNullException.ThrowIfNull(document);

        var typeId = parameter.GetTypeId();
        var isBuiltIn = !typeId.Empty() && ParameterUtils.IsBuiltInParameter(typeId);
        return ParameterIdentityClassifier.Classify(
            isBuiltIn,
            isBuiltIn ? typeId.TypeId : null,
            parameter.IsShared,
            parameter.IsShared ? parameter.GUID : Guid.Empty,
            ResolveLocalKey(parameter, document));
    }

    public static DescribeParameterDataType ClassifyDataType(Parameter parameter)
    {
        ArgumentNullException.ThrowIfNull(parameter);

        var dataType = parameter.Definition?.GetDataType();
        if (dataType is null || dataType.Empty())
        {
            return ParameterDataTypeClassifier.Classify(null, false, false, false);
        }

        return ParameterDataTypeClassifier.Classify(
            dataType.TypeId,
            UnitUtils.IsMeasurableSpec(dataType),
            Category.IsBuiltInCategory(dataType),
            SpecUtils.IsSpec(dataType));
    }

    private static string ResolveLocalKey(Parameter parameter, Document document)
    {
        if (parameter.Definition is InternalDefinition definition)
        {
            if (document.GetElement(definition.Id) is ParameterElement parameterElement
                && !string.IsNullOrEmpty(parameterElement.UniqueId))
            {
                return parameterElement.UniqueId;
            }

            return "local:" + definition.Id.Value.ToString(CultureInfo.InvariantCulture);
        }

        return "local:" + parameter.Id.Value.ToString(CultureInfo.InvariantCulture);
    }
}
