using System.Drawing;
using System.Drawing.Imaging;

namespace Cress.Companion;

public sealed class ScreenPreviewProvider : ICompanionPreviewProvider
{
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

        using var bitmap = new Bitmap(width, height);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, new Size(width, height));
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return $"data:image/png;base64,{Convert.ToBase64String(stream.ToArray())}";
    }
}
