namespace RevitMCP.Bridge;

public sealed class BridgeException : Exception
{
    public BridgeException(string errorCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }

    public string ErrorCode { get; }
}
