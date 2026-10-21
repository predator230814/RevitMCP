using SkiaSharp;

namespace BimIt.Revit.Assistant;

internal static class DocumentCalibrationPreview
{
    public const int MaxPreviewBytes = 128 * 1024;

    public static bool TryCreate(AssistantVisualPage page, out string mimeType, out string imageBase64)
    {
        mimeType = "";
        imageBase64 = "";
        if (page.RasterWidth <= 0 || page.RasterHeight <= 0 || page.Bytes.Length == 0)
        {
            return false;
        }

        using var bitmap = SKBitmap.Decode(page.Bytes);
        if (bitmap is null || bitmap.Width != page.RasterWidth || bitmap.Height != page.RasterHeight)
        {
            return false;
        }

        var edge = Math.Max(bitmap.Width, bitmap.Height);
        while (edge >= 1)
        {
            using var scaled = Scale(bitmap, page.RasterWidth, page.RasterHeight, edge);
            if (scaled is not null)
            {
                foreach (var quality in new[] { 80, 60, 45, 30 })
                {
                    if (TryAccept(scaled, SKEncodedImageFormat.Jpeg, quality, "image/jpeg", ref mimeType, ref imageBase64))
                    {
                        return true;
                    }
                }

                if (TryAccept(scaled, SKEncodedImageFormat.Png, 100, "image/png", ref mimeType, ref imageBase64))
                {
                    return true;
                }
            }

            if (edge == 1)
            {
                break;
            }

            var next = Math.Max(1, edge * 3 / 4);
            edge = next >= edge ? edge - 1 : next;
        }

        return false;
    }

    private static bool TryAccept(
        SKBitmap bitmap,
        SKEncodedImageFormat format,
        int quality,
        string mime,
        ref string mimeType,
        ref string imageBase64)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, quality);
        if (data is null || data.Size is <= 0 or > MaxPreviewBytes)
        {
            return false;
        }

        var bytes = data.ToArray();
        if (bytes.Length is <= 0 or > MaxPreviewBytes)
        {
            return false;
        }

        mimeType = mime;
        imageBase64 = Convert.ToBase64String(bytes);
        return true;
    }

    private static SKBitmap? Scale(SKBitmap source, int rasterWidth, int rasterHeight, int longEdge)
    {
        int width;
        int height;
        if (rasterWidth >= rasterHeight)
        {
            width = longEdge;
            height = Math.Max(1, (int)Math.Round(longEdge * (rasterHeight / (double)rasterWidth)));
        }
        else
        {
            height = longEdge;
            width = Math.Max(1, (int)Math.Round(longEdge * (rasterWidth / (double)rasterHeight)));
        }

        if (width <= 0 || height <= 0)
        {
            return null;
        }

        var destination = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque));
        using var canvas = new SKCanvas(destination);
        canvas.Clear(SKColors.White);
        canvas.DrawBitmap(source, new SKRect(0, 0, width, height), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
        return destination;
    }
}
