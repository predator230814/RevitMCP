using SkiaSharp;

namespace BimIt.Revit.Assistant.Patterns;

internal static class RoomPatternReviewRaster
{
    public const int MaxBytes = 48_000;

    public const int LongEdge = 960;

    public static bool TryCreate(byte[] source, out string mimeType, out string base64)
    {
        mimeType = "image/jpeg";
        base64 = "";
        if (source is null || source.Length == 0)
        {
            return false;
        }

        using var bitmap = Decode(source);
        if (bitmap is null || bitmap.Width <= 0 || bitmap.Height <= 0)
        {
            return false;
        }

        using var scaled = Scale(bitmap, LongEdge);
        foreach (var quality in new[] { 70, 55, 40, 30 })
        {
            using var image = SKImage.FromBitmap(scaled);
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, quality);
            if (data is null || data.Size is <= 0 or > MaxBytes)
            {
                continue;
            }

            base64 = Convert.ToBase64String(data.ToArray());
            return base64.Length <= 70_000;
        }

        return false;
    }

    private static SKBitmap? Decode(byte[] source)
    {
        try
        {
            return SKBitmap.Decode(source);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static SKBitmap Scale(SKBitmap source, int longEdge)
    {
        var edge = Math.Max(source.Width, source.Height);
        if (edge <= longEdge)
        {
            return source.Copy();
        }

        var scale = longEdge / (double)edge;
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));
        var destination = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque));
        using var canvas = new SKCanvas(destination);
        canvas.Clear(SKColors.White);
        canvas.DrawBitmap(source, new SKRect(0, 0, width, height), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
        return destination;
    }
}
