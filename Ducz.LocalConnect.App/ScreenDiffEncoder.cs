using System.Drawing.Imaging;

namespace Ducz.LocalConnect.App;

internal sealed class ScreenDiffEncoder : IDisposable
{
    private const int FullFrameInterval = 20;
    private const int GridSize = 32;

    private Bitmap? _previousFrame;
    private int _frameCounter;

    public RemoteFrame? CaptureNextFrame()
    {
        var bounds = Screen.PrimaryScreen?.Bounds ?? throw new InvalidOperationException("Nenhuma tela principal foi encontrada.");
        using var currentFrame = new Bitmap(bounds.Width, bounds.Height);
        using (var graphics = Graphics.FromImage(currentFrame))
        {
            graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
        }

        if (_previousFrame is null || _frameCounter++ % FullFrameInterval == 0)
        {
            return CreateFullFrame(currentFrame);
        }

        var dirtyBounds = DetectDirtyBounds(_previousFrame, currentFrame);
        if (dirtyBounds.IsEmpty)
        {
            _previousFrame.Dispose();
            _previousFrame = (Bitmap)currentFrame.Clone();
            return null;
        }

        using var patch = new Bitmap(dirtyBounds.Width, dirtyBounds.Height);
        using (var graphics = Graphics.FromImage(patch))
        {
            graphics.DrawImage(currentFrame, new Rectangle(0, 0, patch.Width, patch.Height), dirtyBounds, GraphicsUnit.Pixel);
        }

        var imageBytes = EncodeJpeg(patch, 74L);

        _previousFrame.Dispose();
        _previousFrame = (Bitmap)currentFrame.Clone();

        return new RemoteFrame(RemoteFrameKind.Delta, bounds.Width, bounds.Height, dirtyBounds.X, dirtyBounds.Y, dirtyBounds.Width, dirtyBounds.Height, imageBytes);
    }

    public void Dispose()
    {
        _previousFrame?.Dispose();
    }

    private RemoteFrame CreateFullFrame(Bitmap currentFrame)
    {
        var imageBytes = EncodeJpeg(currentFrame, 65L);
        _previousFrame?.Dispose();
        _previousFrame = (Bitmap)currentFrame.Clone();
        return new RemoteFrame(RemoteFrameKind.Full, currentFrame.Width, currentFrame.Height, 0, 0, currentFrame.Width, currentFrame.Height, imageBytes);
    }

    private static Rectangle DetectDirtyBounds(Bitmap previousFrame, Bitmap currentFrame)
    {
        var minX = int.MaxValue;
        var minY = int.MaxValue;
        var maxX = -1;
        var maxY = -1;

        for (var y = 0; y < currentFrame.Height; y += GridSize)
        {
            for (var x = 0; x < currentFrame.Width; x += GridSize)
            {
                var sampleX = Math.Min(x + (GridSize / 2), currentFrame.Width - 1);
                var sampleY = Math.Min(y + (GridSize / 2), currentFrame.Height - 1);
                var previousColor = previousFrame.GetPixel(sampleX, sampleY);
                var currentColor = currentFrame.GetPixel(sampleX, sampleY);
                var difference = Math.Abs(previousColor.R - currentColor.R)
                    + Math.Abs(previousColor.G - currentColor.G)
                    + Math.Abs(previousColor.B - currentColor.B);

                if (difference < 15)
                {
                    continue;
                }

                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, Math.Min(x + GridSize, currentFrame.Width));
                maxY = Math.Max(maxY, Math.Min(y + GridSize, currentFrame.Height));
            }
        }

        if (maxX < 0 || maxY < 0)
        {
            return Rectangle.Empty;
        }

        return Rectangle.FromLTRB(minX, minY, maxX, maxY);
    }

    private static byte[] EncodeJpeg(Bitmap bitmap, long quality)
    {
        using var stream = new MemoryStream();
        var encoder = ImageCodecInfo.GetImageEncoders().First(codec => codec.FormatID == ImageFormat.Jpeg.Guid);
        using var encoderParameters = new EncoderParameters(1);
        encoderParameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
        bitmap.Save(stream, encoder, encoderParameters);
        return stream.ToArray();
    }
}