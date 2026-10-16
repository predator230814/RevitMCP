using PDFtoImage;
using SkiaSharp;

namespace BimIt.Revit.Assistant;

internal static class AssistantVisualReader
{
    public static AssistantDocumentReadResult ReadPdf(string path, string displayName, long size)
    {
        try
        {
            using var stream = AssistantDocumentReader.OpenRead(path);
            var header = new byte[5];
            var read = stream.Read(header, 0, header.Length);
            if (read < 5 || header[0] != (byte)'%' || header[1] != (byte)'P' || header[2] != (byte)'D' || header[3] != (byte)'F' || header[4] != (byte)'-')
            {
                return Invalid();
            }

            stream.Position = 0;
            var pageCount = Conversion.GetPageCount(stream, leaveOpen: true);
            if (pageCount <= 0)
            {
                return Invalid();
            }

            var renderCount = Math.Min(pageCount, AssistantDocumentLimits.MaxVisualPages);
            var pages = new List<AssistantVisualPage>(renderCount);
            for (var index = 0; index < renderCount; index++)
            {
                stream.Position = 0;
                var pageSize = Conversion.GetPageSize(stream, index, leaveOpen: true);
                var options = RenderOptionsFor(pageSize.Width, pageSize.Height);
                stream.Position = 0;
                using var bitmap = Conversion.ToImage(stream, index, leaveOpen: true, password: null, options: options);
                if (bitmap is null || bitmap.Width <= 0 || bitmap.Height <= 0 || !AssistantVisualEncoder.TryEncode(bitmap, out var bytes, out var mimeType))
                {
                    return PdfFailed();
                }

                pages.Add(new AssistantVisualPage(index + 1, mimeType, bytes));
            }

            return AssistantDocumentReadResult.Success(new AssistantDocumentArtifact(
                displayName,
                AssistantDocumentKind.Pdf,
                size,
                null,
                false,
                pages,
                pageCount > renderCount,
                pageCount));
        }
        catch (PDFtoImage.Exceptions.PdfInvalidFormatException)
        {
            return Invalid();
        }
        catch (PDFtoImage.Exceptions.PdfException)
        {
            return PdfFailed();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ReadFailed();
        }
        catch (Exception)
        {
            return PdfFailed();
        }
    }

    public static AssistantDocumentReadResult ReadImage(string path, string displayName, long size)
    {
        try
        {
            using var stream = AssistantDocumentReader.OpenRead(path);
            using var codec = SKCodec.Create(stream);
            if (codec is null || !AssistantVisualBounds.TrySelectDecode(codec, out var target))
            {
                return Invalid();
            }

            using var bitmap = SKBitmap.Decode(codec, target);
            if (bitmap is null
                || bitmap.Width != target.Width
                || bitmap.Height != target.Height
                || !AssistantVisualBounds.Fits(bitmap.Width, bitmap.Height)
                || !AssistantVisualEncoder.TryEncode(bitmap, out var bytes, out var mimeType))
            {
                return Invalid();
            }

            return AssistantDocumentReadResult.Success(new AssistantDocumentArtifact(
                displayName,
                AssistantDocumentKind.Image,
                size,
                null,
                false,
                [new AssistantVisualPage(1, mimeType, bytes)],
                false,
                1));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ReadFailed();
        }
        catch (Exception)
        {
            return Invalid();
        }
    }

    private static RenderOptions RenderOptionsFor(double width, double height)
    {
        int? targetWidth = null;
        int? targetHeight = null;
        if (width >= height)
        {
            targetWidth = AssistantDocumentLimits.VisualLongEdge;
        }
        else
        {
            targetHeight = AssistantDocumentLimits.VisualLongEdge;
        }

        return new RenderOptions(
            Width: targetWidth,
            Height: targetHeight,
            WithAnnotations: true,
            WithFormFill: true,
            WithAspectRatio: true,
            BackgroundColor: SKColors.White);
    }

    private static AssistantDocumentReadResult Invalid()
    {
        return AssistantDocumentReadResult.Failure(AssistantDocumentErrors.Invalid, AssistantDocumentErrors.InvalidMessage);
    }

    private static AssistantDocumentReadResult ReadFailed()
    {
        return AssistantDocumentReadResult.Failure(AssistantDocumentErrors.ReadFailed, AssistantDocumentErrors.ReadFailedMessage);
    }

    private static AssistantDocumentReadResult PdfFailed()
    {
        return AssistantDocumentReadResult.Failure(AssistantDocumentErrors.PdfRenderFailed, AssistantDocumentErrors.PdfRenderFailedMessage);
    }
}

internal static class AssistantVisualBounds
{
    public static bool TrySelectDecode(SKCodec codec, out SKImageInfo info)
    {
        return TrySelectDecode(codec.Info.Width, codec.Info.Height, codec.GetScaledDimensions, out info);
    }

    public static bool TrySelectDecode(int sourceWidth, int sourceHeight, Func<float, SKSizeI> scaledDimensions, out SKImageInfo info)
    {
        info = default;
        if (sourceWidth <= 0
            || sourceHeight <= 0
            || sourceWidth > AssistantDocumentLimits.MaxSourceEdge
            || sourceHeight > AssistantDocumentLimits.MaxSourceEdge)
        {
            return false;
        }

        var longEdge = Math.Max(sourceWidth, sourceHeight);
        var scale = longEdge <= AssistantDocumentLimits.VisualLongEdge
            ? 1f
            : AssistantDocumentLimits.VisualLongEdge / (float)longEdge;
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var scaled = scaledDimensions(scale);
            if (Fits(scaled.Width, scaled.Height))
            {
                info = new SKImageInfo(scaled.Width, scaled.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
                return true;
            }

            var next = scale * 0.5f;
            if (next >= scale || next < 1f / AssistantDocumentLimits.MaxSourceEdge)
            {
                break;
            }

            scale = next;
        }

        return false;
    }

    public static bool Fits(int width, int height)
    {
        if (width <= 0 || height <= 0 || width > AssistantDocumentLimits.MaxDecodeEdge || height > AssistantDocumentLimits.MaxDecodeEdge)
        {
            return false;
        }

        return (long)width * height * AssistantDocumentLimits.DecodedBytesPerPixel <= AssistantDocumentLimits.MaxDecodedBytes;
    }
}

internal static class AssistantVisualEncoder
{
    public static bool TryEncode(SKBitmap source, out byte[] bytes, out string mimeType)
    {
        bytes = [];
        mimeType = "image/png";
        using var prepared = Scale(source, AssistantDocumentLimits.VisualLongEdge);
        if (TryFit(prepared, out bytes, out mimeType))
        {
            return true;
        }

        var edge = Math.Max(prepared.Width, prepared.Height);
        while (edge > 320)
        {
            var next = Math.Max(320, edge * 3 / 4);
            if (next >= edge)
            {
                break;
            }

            using var smaller = Scale(prepared, next);
            if (TryFit(smaller, out bytes, out mimeType))
            {
                return true;
            }

            edge = Math.Max(smaller.Width, smaller.Height);
        }

        return false;
    }

    private static bool TryFit(SKBitmap bitmap, out byte[] bytes, out string mimeType)
    {
        var png = Encode(bitmap, SKEncodedImageFormat.Png, 100);
        if (png.Length > 0 && png.Length <= AssistantDocumentLimits.MaxVisualBytes)
        {
            bytes = png;
            mimeType = "image/png";
            return true;
        }

        foreach (var quality in new[] { 85, 70, 55, 40 })
        {
            var jpeg = Encode(bitmap, SKEncodedImageFormat.Jpeg, quality);
            if (jpeg.Length > 0 && jpeg.Length <= AssistantDocumentLimits.MaxVisualBytes)
            {
                bytes = jpeg;
                mimeType = "image/jpeg";
                return true;
            }
        }

        bytes = [];
        mimeType = "image/png";
        return false;
    }

    private static SKBitmap Scale(SKBitmap source, int longEdge)
    {
        var edge = Math.Max(source.Width, source.Height);
        if (edge <= longEdge || edge <= 0)
        {
            return source.Copy();
        }

        var scale = longEdge / (double)edge;
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));
        var destination = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(destination);
        canvas.Clear(SKColors.White);
        canvas.DrawBitmap(source, new SKRect(0, 0, width, height), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
        return destination;
    }

    private static byte[] Encode(SKBitmap bitmap, SKEncodedImageFormat format, int quality)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, quality);
        return data is null ? [] : data.ToArray();
    }
}
