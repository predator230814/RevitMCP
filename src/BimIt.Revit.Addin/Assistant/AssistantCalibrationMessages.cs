using System.Text.Json;
using BimIt.Revit.Assistant.Ipc;

namespace BimIt.Revit.Addin.Assistant;

internal static class AssistantCalibrationMessages
{
    public const string Closed = """{"type":"calibrationClosed"}""";

    private static readonly string[] WebViewFields = ["type", "sessionRef", "mimeType", "image", "rasterWidth", "rasterHeight"];

    public static string? ForWebView(AssistantIpcMessage message)
    {
        if (message.Kind != AssistantIpcKind.CalibrationPage
            || string.IsNullOrEmpty(message.CalibrationSession)
            || message.CalibrationMime is not ("image/jpeg" or "image/png")
            || string.IsNullOrEmpty(message.CalibrationImage)
            || message.RasterWidth is not > 0
            || message.RasterHeight is not > 0
            || message.CalibrationImage.Contains('\\', StringComparison.Ordinal)
            || message.CalibrationImage.Contains("://", StringComparison.Ordinal))
        {
            return null;
        }

        var payload = new Dictionary<string, object?>
        {
            ["type"] = "calibrationPage",
            ["sessionRef"] = message.CalibrationSession,
            ["mimeType"] = message.CalibrationMime,
            ["image"] = message.CalibrationImage,
            ["rasterWidth"] = message.RasterWidth,
            ["rasterHeight"] = message.RasterHeight,
        };
        var json = JsonSerializer.Serialize(payload);
        try
        {
            using var document = JsonDocument.Parse(json);
            var names = document.RootElement.EnumerateObject().Select(property => property.Name).ToArray();
            if (names.Length != WebViewFields.Length || names.Except(WebViewFields, StringComparer.Ordinal).Any())
            {
                return null;
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return json;
    }
}
