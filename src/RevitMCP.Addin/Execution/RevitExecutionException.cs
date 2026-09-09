namespace RevitMCP.Addin.Execution;

public sealed class RevitExecutionException : Exception
{
    public RevitExecutionException(string errorCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }

    public string ErrorCode { get; }
}
