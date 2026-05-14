using System.Drawing;
using System.Drawing.Imaging;

namespace Cress.Companion;

internal delegate byte[] ScreenPreviewCapture(int left, int top, int width, int height);

public sealed class ScreenPreviewProvider : ICompanionPreviewProvider
{
    private readonly ScreenPreviewCapture _capture;

    public ScreenPreviewProvider()
        : this(CapturePng)
    {
    }

    internal ScreenPreviewProvider(ScreenPreviewCapture capture)
    {
        _capture = capture;
    }

    public string? CapturePreview(CompanionWindowBounds bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return null;
        }

        var width = Math.Min(bounds.Width, 640);
        var height = Math.Min(bounds.Height, 360);
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        var bytes = _capture(bounds.Left, bounds.Top, width, height);
        return $"data:image/png;base64,{Convert.ToBase64String(bytes)}";
    }

    private static byte[] CapturePng(int left, int top, int width, int height)
    {
        using var bitmap = new Bitmap(width, height);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(left, top, 0, 0, new Size(width, height));
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }
}
