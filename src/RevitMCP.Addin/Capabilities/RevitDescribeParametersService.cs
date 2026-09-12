using System.Globalization;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCP.Addin.Execution;
using RevitMCP.Addin.Identity;
using RevitMCP.Addin.Inspection;
using RevitMCP.Bridge;
using RevitMCP.Contracts;

namespace RevitMCP.Addin.Capabilities;

internal sealed class RevitDescribeParametersService : IRevitDescribeParametersService
{
    private readonly RevitExecutionDispatcher _dispatcher;
    private readonly BridgeInstanceMetadata _metadata;
    private readonly OpenDocumentIdentityService _identity;
    private readonly OpenDocumentParameterIdentityService _parameterRefs;

    public RevitDescribeParametersService(
        RevitExecutionDispatcher dispatcher,
        BridgeInstanceMetadata metadata,
        OpenDocumentIdentityService identity,
        OpenDocumentParameterIdentityService parameterRefs)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(parameterRefs);
        _dispatcher = dispatcher;
        _metadata = metadata;
        _identity = identity;
        _parameterRefs = parameterRefs;
    }

    public async Task<DescribeParametersResult> DescribeParametersAsync(
        DescribeParametersRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        DescribeParametersRequestValidator.Validate(request);

        try
        {
            return await _dispatcher.EnqueueAsync(application => Execute(application, request), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (BridgeException)
        {
            throw;
        }
        catch (RevitExecutionException)
        {
            throw new BridgeException(
                CapabilityErrorCodes.ExecutionFailed,
                "The Revit parameter discovery could not be executed.");
        }
        catch (Exception)
        {
            throw new BridgeException(
                CapabilityErrorCodes.ExecutionFailed,
                "The Revit parameter discovery could not be executed.");
        }
    }

    private DescribeParametersResult Execute(UIApplication application, DescribeParametersRequest request)
    {
        var uiDocument = application.ActiveUIDocument;
        if (uiDocument is null)
        {
            throw new BridgeException(
                CapabilityErrorCodes.NoActiveDocument,
                "No active Revit document is available.");
        }

        var document = uiDocument.Document;
        var documentId = _identity.GetId(document);
        if (!string.Equals(request.DocumentId, documentId, StringComparison.Ordinal))
        {
            throw new BridgeException(
                CapabilityErrorCodes.DocumentContextChanged,
                "The supplied document_id does not match the active document.");
        }

        var metadata = new ElementBasicMetadataResolver(document);
        var resolved = new HashSet<string>(StringComparer.Ordinal);
        var occurrences = new List<DescribeParameterOccurrence>();

        foreach (var requestedRef in request.ElementRefs)
        {
            var element = document.GetElement(requestedRef);
            if (element is null || element is ElementType)
            {
                continue;
            }

            resolved.Add(requestedRef);
            var seen = new HashSet<string>(StringComparer.Ordinal);

            if (request.Source is DescribeParameterSource.Instance or DescribeParameterSource.Both)
            {
                Collect(document, requestedRef, element, GetElementParameterSource.Instance, seen, occurrences);
            }

            if (request.Source is DescribeParameterSource.Type or DescribeParameterSource.Both)
            {
                var type = metadata.ResolveType(element);
                if (type is not null)
                {
                    Collect(document, requestedRef, type, GetElementParameterSource.Type, seen, occurrences);
                }
            }
        }

        return DescribeParametersShaper.Shape(
            new DescribeParametersContext
            {
                InstanceId = _metadata.InstanceId,
                DocumentId = documentId
            },
            request.ElementRefs,
            resolved,
            occurrences,
            request.NameContains,
            request.Limit);
    }

    private void Collect(
        Document document,
        string elementRef,
        Element surface,
        GetElementParameterSource source,
        HashSet<string> seen,
        List<DescribeParameterOccurrence> occurrences)
    {
        foreach (var parameter in surface.GetOrderedParameters())
        {
            var name = parameter.Definition?.Name;
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            var classified = ClassifyIdentity(parameter, document);
            var parameterRef = _parameterRefs.GetRef(document, source, classified);
            if (!seen.Add(parameterRef))
            {
                continue;
            }

            occurrences.Add(new DescribeParameterOccurrence
            {
                ParameterRef = parameterRef,
                Name = name,
                Source = source,
                Identity = ParameterIdentityClassifier.ToContract(classified),
                DataType = ClassifyDataType(parameter),
                ElementRef = elementRef,
                IsReadOnly = parameter.IsReadOnly
            });
        }
    }

    private static ClassifiedParameterIdentity ClassifyIdentity(Parameter parameter, Document document)
    {
        var typeId = parameter.GetTypeId();
        var isBuiltIn = !typeId.Empty() && ParameterUtils.IsBuiltInParameter(typeId);
        return ParameterIdentityClassifier.Classify(
            isBuiltIn,
            isBuiltIn ? typeId.TypeId : null,
            parameter.IsShared,
            parameter.IsShared ? parameter.GUID : Guid.Empty,
            ResolveLocalKey(parameter, document));
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

    private static DescribeParameterDataType ClassifyDataType(Parameter parameter)
    {
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
}
