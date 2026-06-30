using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace ClipboardManager;

/// <summary>使用 Windows.Media.Ocr 识别图片中的文字（离线、系统组件）。</summary>
internal static class ImageOcrService
{
    public static async Task<string?> RecognizePngAsync(byte[] pngBytes, CancellationToken cancellationToken = default)
    {
        if (pngBytes is not { Length: > 0 }) return null;
        if (!OcrLanguageInstaller.TryCreateEngine(out var engine) || engine == null)
            return null;

        SoftwareBitmap? bitmap = null;
        try
        {
            bitmap = await DecodePngToSoftwareBitmapAsync(pngBytes, cancellationToken).ConfigureAwait(false);
            if (bitmap == null) return null;

            var result = await engine.RecognizeAsync(bitmap).AsTask(cancellationToken).ConfigureAwait(false);
            var text = OcrTextPostProcessor.FormatResult(result);
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            return null;
        }
        finally
        {
            bitmap?.Dispose();
        }
    }

    private static async Task<SoftwareBitmap?> DecodePngToSoftwareBitmapAsync(
        byte[] pngBytes,
        CancellationToken cancellationToken)
    {
        using var stream = new InMemoryRandomAccessStream();
        await stream.WriteAsync(pngBytes.AsBuffer()).AsTask(cancellationToken).ConfigureAwait(false);
        stream.Seek(0);

        var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(cancellationToken).ConfigureAwait(false);
        uint maxDim;
        try { maxDim = OcrEngine.MaxImageDimension; }
        catch { maxDim = 3200; }
        if (maxDim < 512) maxDim = 3200;

        var w = decoder.PixelWidth;
        var h = decoder.PixelHeight;
        BitmapTransform? transform = null;
        if (w > maxDim || h > maxDim)
        {
            var scale = Math.Min((double)maxDim / w, (double)maxDim / h);
            transform = new BitmapTransform
            {
                ScaledWidth = Math.Max(1, (uint)Math.Round(w * scale)),
                ScaledHeight = Math.Max(1, (uint)Math.Round(h * scale)),
                InterpolationMode = BitmapInterpolationMode.Linear
            };
        }

        var bitmap = transform == null
            ? await decoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied).AsTask(cancellationToken).ConfigureAwait(false)
            : await decoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                transform,
                ExifOrientationMode.RespectExifOrientation,
                ColorManagementMode.DoNotColorManage).AsTask(cancellationToken).ConfigureAwait(false);

        if (bitmap.BitmapPixelFormat is not (BitmapPixelFormat.Bgra8 or BitmapPixelFormat.Gray8))
        {
            var converted = SoftwareBitmap.Convert(bitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
            bitmap.Dispose();
            return converted;
        }

        return bitmap;
    }
}
