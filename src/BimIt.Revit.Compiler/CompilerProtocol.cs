namespace BimIt.Revit.Compiler;

public static class CompilerProtocol
{
    public const int ContractVersion = 1;

    public const int PolicyVersion = 2;

    public const string ContractName = "revit.run_csharp_query";

    public const string SupportedApiName = "RevitAPI";

    public const string SupportedApiVersion = "26.5.0.0";

    public const int MaxSourceCharacters = 8_000;

    public const int MaxDiagnostics = 8;

    public const int MaxDiagnosticMessageCharacters = 180;

    public const int MaxRequestBytes = 32_768;

    public const int MaxResponseBytes = 262_144;

    public const int TimeoutMilliseconds = 30_000;

    public const string RelativeDirectory = "compiler";

    public const string ExecutableFileName = "BimIt.Revit.CompilerHost.exe";

    public const string GeneratedNamespace = "BimIt.Revit.GeneratedQuery";

    public const string GeneratedTypeName = "GeneratedQuery";

    public const string GeneratedMethodName = "Execute";

    public const string ChargeIdentifier = "__bimit_charge";

    public const string ReservedPrefix = "__bimit";

    public const string QueryPath = "query.cs";
}

public static class CompilerOutcomes
{
    public const string Success = "success";

    public const string PolicyRejected = "policy_rejected";

    public const string CompileFailed = "compile_failed";

    public const string ApiUnsupported = "api_unsupported";

    public const string IdentityRejected = "identity_rejected";

    public const string InvalidRequest = "invalid_request";
}

public static class CompilerResultCodes
{
    public const string ApiUnsupported = "CSHARP_QUERY_API_UNSUPPORTED";

    public const string InvalidSource = "INVALID_CSHARP_QUERY";

    public const string PolicyRejected = "CSHARP_QUERY_POLICY_REJECTED";

    public const string CompileFailed = "CSHARP_QUERY_COMPILE_FAILED";

    public const string CompilerFailed = "CSHARP_QUERY_COMPILER_FAILED";
}
