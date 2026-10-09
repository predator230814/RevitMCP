namespace RevitMCP.Contracts;

public static class CapabilityErrorCodes
{
    public const string ExecutionTimeout = "REVIT_EXECUTION_TIMEOUT";

    public const string ExecutionFailed = "REVIT_EXECUTION_FAILED";

    public const string NoActiveDocument = "NO_ACTIVE_DOCUMENT";

    public const string DocumentContextChanged = "DOCUMENT_CONTEXT_CHANGED";

    public const string NoActiveView = "NO_ACTIVE_VIEW";

    public const string InvalidQuery = "INVALID_QUERY";

    public const string InvalidInspection = "INVALID_INSPECTION";

    public const string InvalidParameterDiscovery = "INVALID_PARAMETER_DISCOVERY";

    public const string InvalidParameterRead = "INVALID_PARAMETER_READ";

    public const string InvalidMepTopology = "INVALID_MEP_TOPOLOGY";

    public const string UnsupportedDocumentKind = "UNSUPPORTED_DOCUMENT_KIND";

    public const string DocumentNotWritable = "DOCUMENT_NOT_WRITABLE";

    public const string InvalidParameterUpdatePreview = "INVALID_PARAMETER_UPDATE_PREVIEW";

    public const string IntentCapacityReached = "INTENT_CAPACITY_REACHED";

    public const string InvalidApprovalReviewRequest = "INVALID_APPROVAL_REVIEW_REQUEST";

    public const string InvalidApplyRequest = "INVALID_APPLY_REQUEST";

    public const string InvalidWarnings = "INVALID_WARNINGS";
}
