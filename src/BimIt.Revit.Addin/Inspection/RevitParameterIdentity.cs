using Autodesk.Revit.DB;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Addin.Inspection;

/// <summary>
/// Revit API adapter for CAP-0004 parameter identity and data type.
/// The <see cref="Parameter"/> path (CAP-0004/CAP-0005/CAP-0007) and the
/// <see cref="InternalDefinition"/> path (CAP-0015) read different Revit objects
/// but share the built-in gate, the local stable-key rule, the data-type rule,
/// and the single ordering in <see cref="ParameterIdentityClassifier"/>.
/// </summary>
internal static class RevitParameterIdentity
{
    public static ClassifiedParameterIdentity Classify(Parameter parameter, Document document)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        ArgumentNullException.ThrowIfNull(document);

        var typeId = parameter.GetTypeId();
        var builtInTypeId = BuiltInTypeId(typeId);
        return ParameterIdentityClassifier.Classify(
            builtInTypeId is not null,
            builtInTypeId,
            parameter.IsShared,
            parameter.IsShared ? parameter.GUID : Guid.Empty,
            ResolveLocalKey(parameter, document));
    }

    /// <summary>
    /// Classifies a definition with no element, such as a <c>Document.ParameterBindings</c> key
    /// (CAP-0015 "Definition classification"). <c>GetParameterTypeId()</c> may be null for
    /// custom parameters; <c>ParameterUtils.IsBuiltInParameter</c> is never called for a null or empty id.
    /// </summary>
    public static ClassifiedParameterIdentity Classify(InternalDefinition definition, Document document)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(document);

        var builtInTypeId = BuiltInTypeId(definition.GetParameterTypeId());
        return ParameterIdentityClassifier.ClassifyDefinition(
            builtInTypeId,
            () =>
            {
                // Reached only for a non-built-in definition, whose Id is its ParameterElement.
                var definitionElement = document.GetElement(definition.Id);
                Guid? sharedGuid = definitionElement is SharedParameterElement shared ? shared.GuidValue : null;
                return new DefinitionElementFacts(sharedGuid, ResolveLocalKey(definition, definitionElement));
            });
    }

    public static DescribeParameterDataType ClassifyDataType(Parameter parameter)
    {
        ArgumentNullException.ThrowIfNull(parameter);

        var definition = parameter.Definition;
        return definition is null ? ClassifyDataType((ForgeTypeId?)null) : ClassifyDataType(definition);
    }

    public static DescribeParameterDataType ClassifyDataType(Definition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return ClassifyDataType(definition.GetDataType());
    }

    private static DescribeParameterDataType ClassifyDataType(ForgeTypeId? dataType)
    {
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

    /// <summary>
    /// The null-safe built-in gate shared by both paths: the type id when it is a
    /// built-in parameter, otherwise null.
    /// </summary>
    private static string? BuiltInTypeId(ForgeTypeId? typeId)
    {
        var text = typeId is null || typeId.Empty() ? null : typeId.TypeId;
        return ParameterIdentityClassifier.IsBuiltIn(text, () => ParameterUtils.IsBuiltInParameter(typeId!))
            ? text
            : null;
    }

    private static string ResolveLocalKey(Parameter parameter, Document document)
    {
        if (parameter.Definition is InternalDefinition definition)
        {
            return ResolveLocalKey(definition, document.GetElement(definition.Id));
        }

        return ParameterIdentityClassifier.LocalStableKey(null, parameter.Id.Value);
    }

    private static string ResolveLocalKey(InternalDefinition definition, Element? definitionElement)
    {
        return ParameterIdentityClassifier.LocalStableKey(
            (definitionElement as ParameterElement)?.UniqueId,
            definition.Id.Value);
    }
}
