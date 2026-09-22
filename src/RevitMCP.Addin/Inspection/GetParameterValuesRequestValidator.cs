using RevitMCP.Bridge;
using RevitMCP.Contracts;

namespace RevitMCP.Addin.Inspection;

internal static class GetParameterValuesRequestValidator
{
    public const int MinReads = 1;
    public const int MaxReads = 50;

    public static void Validate(GetParameterValuesRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.DocumentId is null)
        {
            throw InvalidRead();
        }

        if (request.Reads is null || request.Reads.Count is < MinReads or > MaxReads)
        {
            throw InvalidRead();
        }

        var pairs = new HashSet<(string ElementRef, string ParameterRef)>();
        foreach (var read in request.Reads)
        {
            if (read is null || read.ElementRef is null || read.ParameterRef is null)
            {
                throw InvalidRead();
            }

            if (!pairs.Add((read.ElementRef, read.ParameterRef)))
            {
                throw InvalidRead();
            }
        }
    }

    private static BridgeException InvalidRead()
    {
        return new BridgeException(
            CapabilityErrorCodes.InvalidParameterRead,
            "The parameter value request is invalid.");
    }
}
