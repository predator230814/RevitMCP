using System.Text.Json;
using BimIt.Revit.Contracts;
using StreamJsonRpc;

namespace BimIt.Revit.Bridge.Implementation;

internal static class StreamJsonRpcExceptionMapper
{
    public static LocalRpcException ToLocalRpc(BridgeException exception)
    {
        return new LocalRpcException(exception.Message)
        {
            ErrorCode = -32001,
            ErrorData = new BridgeError
            {
                Code = exception.ErrorCode,
                Message = exception.Message,
                Details = exception.Details
            }
        };
    }

    public static BridgeException FromRemote(
        RemoteInvocationException exception,
        string fallbackCode = BridgeErrorCodes.HandshakeFailed)
    {
        if (TryReadError(exception.ErrorData, out var code, out var message, out var details))
        {
            return details is null
                ? new BridgeException(code, message, exception)
                : new BridgeException(code, message, details.Value, exception);
        }

        return new BridgeException(fallbackCode, exception.Message, exception);
    }

    private static bool TryReadError(object? errorData, out string code, out string message, out JsonElement? details)
    {
        code = string.Empty;
        message = string.Empty;
        details = null;

        if (errorData is null)
        {
            return false;
        }

        if (errorData is BridgeError typed)
        {
            code = typed.Code;
            message = typed.Message;
            details = typed.Details;
            return true;
        }

        try
        {
            var json = errorData is JsonElement element
                ? element.GetRawText()
                : JsonSerializer.Serialize(errorData, ContractJson.Options);
            var parsed = JsonSerializer.Deserialize<BridgeError>(json, ContractJson.Options);
            if (parsed is null || string.IsNullOrWhiteSpace(parsed.Code))
            {
                return false;
            }

            code = parsed.Code;
            message = parsed.Message;
            details = parsed.Details;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
