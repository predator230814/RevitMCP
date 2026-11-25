using System.Text.Json;

namespace BimIt.Revit.Bridge;

public sealed class BridgeException : Exception
{
    public BridgeException(string errorCode, string message, Exception? innerException = null)
        : this(errorCode, message, null, innerException)
    {
    }

    public BridgeException(string errorCode, string message, JsonElement details, Exception? innerException = null)
        : this(errorCode, message, (JsonElement?)details, innerException)
    {
    }

    private BridgeException(string errorCode, string message, JsonElement? details, Exception? innerException)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
        Details = details;
    }

    public string ErrorCode { get; }

    public JsonElement? Details { get; }
}
