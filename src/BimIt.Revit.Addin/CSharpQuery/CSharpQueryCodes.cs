namespace BimIt.Revit.Addin.CSharpQuery;

internal static class CSharpQueryCodes
{
    public const string ApiUnsupported = "CSHARP_QUERY_API_UNSUPPORTED";

    public const string DocumentNotReady = "CSHARP_QUERY_DOCUMENT_NOT_READY";

    public const string BudgetExceeded = "CSHARP_QUERY_BUDGET_EXCEEDED";

    public const string ExecutionFailed = "CSHARP_QUERY_EXECUTION_FAILED";

    public const string InvalidResult = "CSHARP_QUERY_INVALID_RESULT";

    public const string ResultTooLarge = "CSHARP_QUERY_RESULT_TOO_LARGE";

    public const string NoActiveDocument = "no_active_document";

    public const string ProjectDocumentRequired = "project_document_required";

    public const string DocumentMismatch = "document_mismatch";

    public const string DocumentModifiable = "document_modifiable";
}

internal static class CSharpQueryLimits
{
    public const int LoopCharges = 10_000;

    public const int MaxDepth = 8;

    public const int MaxNodes = 1_024;

    public const int MaxSerializedCharacters = 16_000;
}
