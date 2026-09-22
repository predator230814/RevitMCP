using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCP.Addin.Execution;
using RevitMCP.Addin.Identity;
using RevitMCP.Addin.Inspection;
using RevitMCP.Bridge;
using RevitMCP.Contracts;

namespace RevitMCP.Addin.Capabilities;

internal sealed class RevitGetParameterValuesService : IRevitGetParameterValuesService
{
    private readonly RevitExecutionDispatcher _dispatcher;
    private readonly BridgeInstanceMetadata _metadata;
    private readonly OpenDocumentIdentityService _identity;
    private readonly OpenDocumentParameterIdentityService _parameterRefs;

    public RevitGetParameterValuesService(
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

    public async Task<GetParameterValuesResult> GetParameterValuesAsync(
        GetParameterValuesRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        GetParameterValuesRequestValidator.Validate(request);

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
                "The Revit parameter value read could not be executed.");
        }
        catch (Exception)
        {
            throw new BridgeException(
                CapabilityErrorCodes.ExecutionFailed,
                "The Revit parameter value read could not be executed.");
        }
    }

    private GetParameterValuesResult Execute(UIApplication application, GetParameterValuesRequest request)
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
        var items = new List<GetParameterValueItem>(request.Reads.Count);
        foreach (var read in request.Reads)
        {
            items.Add(ReadPair(document, metadata, read));
        }

        return new GetParameterValuesResult
        {
            Context = new DescribeParametersContext
            {
                InstanceId = _metadata.InstanceId,
                DocumentId = documentId
            },
            Items = items
        };
    }

    private GetParameterValueItem ReadPair(
        Document document,
        ElementBasicMetadataResolver metadata,
        GetParameterValueRead read)
    {
        if (!_parameterRefs.TryResolve(document, read.ParameterRef, out var binding))
        {
            return Failure(read, GetParameterValueStatus.ParameterRefNotFound);
        }

        var element = document.GetElement(read.ElementRef);
        if (element is null || element is ElementType)
        {
            return Failure(read, GetParameterValueStatus.ElementNotFound);
        }

        Element surface = element;
        if (binding.Source == GetElementParameterSource.Type)
        {
            var type = metadata.ResolveType(element);
            if (type is null)
            {
                return Failure(read, GetParameterValueStatus.ParameterNotPresent);
            }

            surface = type;
        }

        Parameter? match = null;
        foreach (var parameter in surface.GetOrderedParameters())
        {
            var classified = RevitParameterIdentity.Classify(parameter, document);
            if (classified == binding.Identity)
            {
                match = parameter;
                break;
            }
        }

        if (match is null)
        {
            return Failure(read, GetParameterValueStatus.ParameterNotPresent);
        }

        var dataType = RevitParameterIdentity.ClassifyDataType(match);
        if (!match.HasValue)
        {
            return new GetParameterValueItem
            {
                ElementRef = read.ElementRef,
                ParameterRef = read.ParameterRef,
                Status = GetParameterValueStatus.Ok,
                DataType = dataType,
                HasValue = false
            };
        }

        if (!RevitParameterTypedValueReader.TryRead(match, document, out var value) || value is null)
        {
            return Failure(read, GetParameterValueStatus.UnsupportedValue);
        }

        return new GetParameterValueItem
        {
            ElementRef = read.ElementRef,
            ParameterRef = read.ParameterRef,
            Status = GetParameterValueStatus.Ok,
            DataType = dataType,
            HasValue = true,
            Value = value
        };
    }

    private static GetParameterValueItem Failure(GetParameterValueRead read, GetParameterValueStatus status)
    {
        return new GetParameterValueItem
        {
            ElementRef = read.ElementRef,
            ParameterRef = read.ParameterRef,
            Status = status
        };
    }
}
