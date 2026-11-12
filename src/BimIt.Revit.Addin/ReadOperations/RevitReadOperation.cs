using System.Text.Json;
using BimIt.Revit.Bridge;
using BimIt.Revit.Contracts;

namespace BimIt.Revit.Addin.ReadOperations;

internal abstract class RevitReadOperation<TRequest, TResult> : IExecutableReadOperation
    where TRequest : class
    where TResult : class
{
    public abstract ReadOperationDescriptor Descriptor { get; }

    public object Bind(JsonElement inputs)
    {
        try
        {
            var request = JsonSerializer.Deserialize<TRequest>(inputs, ReadResultBudget.Options);
            if (request is null)
            {
                throw InvalidInput();
            }

            return request;
        }
        catch (BridgeException)
        {
            throw;
        }
        catch (JsonException)
        {
            throw InvalidInput();
        }
        catch (NotSupportedException)
        {
            throw InvalidInput();
        }
    }

    public void ValidateInputs(object request)
    {
        ValidateRequest((TRequest)request);
    }

    public byte[] Fingerprint(object request, string documentId, ReadOperationDescriptor descriptor)
    {
        return CreateFingerprint((TRequest)request, documentId, descriptor);
    }

    public ReadOperationExecutionResult Execute(object request, ReadOperationExecutionContext context)
    {
        return ExecuteRead((TRequest)request, context);
    }

    public void ValidateResult(object result)
    {
        ValidateTypedResult((TResult)result);
    }

    protected abstract void ValidateRequest(TRequest request);

    protected abstract byte[] CreateFingerprint(TRequest request, string documentId, ReadOperationDescriptor descriptor);

    protected abstract ReadOperationExecutionResult ExecuteRead(TRequest request, ReadOperationExecutionContext context);

    protected abstract void ValidateTypedResult(TResult result);

    protected static BridgeException InvalidInput(string message = "The read operation inputs are invalid.")
    {
        return new BridgeException(CapabilityErrorCodes.InvalidReadOperationInput, message);
    }

    protected static BridgeException InvalidResult()
    {
        return new BridgeException(CapabilityErrorCodes.ReadOperationResultInvalid, "The read operation result is invalid.");
    }
}
