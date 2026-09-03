using System.Drawing;

namespace Ducz.LocalConnect.Core.Capture;

public sealed class GdiScreenSource : IScreenSource
{
    public GdiScreenSource(Rectangle bounds)
    {
        Bounds = bounds;
    }

    public Rectangle Bounds { get; }

    public void Capture(Bitmap target)
    {
        using var graphics = Graphics.FromImage(target);
        graphics.CopyFromScreen(Bounds.Location, Point.Empty, Bounds.Size, CopyPixelOperation.SourceCopy);
    }

    public void Dispose()
    {
    }
}
