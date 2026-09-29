using RevitMCP.Bridge;
using RevitMCP.Contracts;

namespace RevitMCP.Addin.Inspection;

internal static class PreviewParameterUpdatesRequestValidator
{
    public const int MinUpdates = 1;
    public const int MaxUpdates = 20;
    public const int MaxStringLength = ParameterValueTextBounder.MaxLength;

    public static void Validate(PreviewParameterUpdatesRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.DocumentId is null)
        {
            throw InvalidPreview();
        }

        if (request.Updates is null || request.Updates.Count is < MinUpdates or > MaxUpdates)
        {
            throw InvalidPreview();
        }

        var pairs = new HashSet<(string ElementRef, string ParameterRef)>();
        foreach (var update in request.Updates)
        {
            if (update is null || update.ElementRef is null || update.ParameterRef is null || update.Value is null)
            {
                throw InvalidPreview();
            }

            if (!pairs.Add((update.ElementRef, update.ParameterRef)))
            {
                throw InvalidPreview();
            }

            ValidateValue(update.Value);
        }
    }

    private static void ValidateValue(PreviewParameterValue value)
    {
        switch (value)
        {
            case PreviewParameterStringValue text when text.Value is not null && text.Value.Length <= MaxStringLength:
                return;
            case PreviewParameterIntegerValue:
                return;
            case PreviewParameterQuantityValue quantity
                when double.IsFinite(quantity.Value) && !string.IsNullOrEmpty(quantity.UnitTypeId):
                return;
            default:
                throw InvalidPreview();
        }
    }

    private static BridgeException InvalidPreview()
    {
        return new BridgeException(
            CapabilityErrorCodes.InvalidParameterUpdatePreview,
            "The parameter update preview request is invalid.");
    }
}
