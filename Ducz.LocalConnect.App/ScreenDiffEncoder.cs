using System.Drawing.Imaging;

namespace Ducz.LocalConnect.App;

internal sealed class ScreenDiffEncoder : IDisposable
{
    private const int FullFrameInterval = 8;
    private const int GridSize = 16;
    private const int PixelThreshold = 18;

    private string? _screenDeviceName;
    private Bitmap? _previousFrame;
    private int _frameCounter;

    public void SetScreen(string? screenDeviceName)
    {
        if (string.Equals(_screenDeviceName, screenDeviceName, StringComparison.Ordinal))
        {
            return;
        }

        _screenDeviceName = screenDeviceName;
        _frameCounter = 0;
        _previousFrame?.Dispose();
        _previousFrame = null;
    }

    public RemoteFrame? CaptureNextFrame()
    {
        var bounds = ResolveScreen().Bounds;
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

        var imageBytes = EncodeJpeg(patch, 72L);

        _previousFrame.Dispose();
        _previousFrame = (Bitmap)currentFrame.Clone();

        return new RemoteFrame(RemoteFrameKind.Delta, bounds.Width, bounds.Height, dirtyBounds.X, dirtyBounds.Y, dirtyBounds.Width, dirtyBounds.Height, imageBytes);
    }

    public void Dispose()
    {
        _previousFrame?.Dispose();
    }

    private Screen ResolveScreen()
    {
        var screens = Screen.AllScreens;
        if (screens.Length == 0)
        {
            throw new InvalidOperationException("No display was found.");
        }

        if (!string.IsNullOrWhiteSpace(_screenDeviceName))
        {
            var selectedScreen = screens.FirstOrDefault(screen => string.Equals(screen.DeviceName, _screenDeviceName, StringComparison.OrdinalIgnoreCase));
            if (selectedScreen is not null)
            {
                return selectedScreen;
            }
        }

        return screens.FirstOrDefault(screen => screen.Primary) ?? screens[0];
    }

    private RemoteFrame CreateFullFrame(Bitmap currentFrame)
    {
        var imageBytes = EncodeJpeg(currentFrame, 58L);
        _previousFrame?.Dispose();
        _previousFrame = (Bitmap)currentFrame.Clone();
        return new RemoteFrame(RemoteFrameKind.Full, currentFrame.Width, currentFrame.Height, 0, 0, currentFrame.Width, currentFrame.Height, imageBytes);
    }

    private static unsafe Rectangle DetectDirtyBounds(Bitmap previousFrame, Bitmap currentFrame)
    {
        var minX = int.MaxValue;
        var minY = int.MaxValue;
        var maxX = -1;
        var maxY = -1;

        var bounds = new Rectangle(0, 0, currentFrame.Width, currentFrame.Height);
        var previousData = previousFrame.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var currentData = currentFrame.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

        try
        {
            for (var y = 0; y < currentFrame.Height; y += GridSize)
            {
                for (var x = 0; x < currentFrame.Width; x += GridSize)
                {
                    if (!HasMeaningfulDifference(previousData, currentData, x, y, currentFrame.Width, currentFrame.Height))
                    {
                        continue;
                    }

                    minX = Math.Min(minX, x);
                    minY = Math.Min(minY, y);
                    maxX = Math.Max(maxX, Math.Min(x + GridSize, currentFrame.Width));
                    maxY = Math.Max(maxY, Math.Min(y + GridSize, currentFrame.Height));
                }
            }
        }
        finally
        {
            previousFrame.UnlockBits(previousData);
            currentFrame.UnlockBits(currentData);
        }

        if (maxX < 0 || maxY < 0)
        {
            return Rectangle.Empty;
        }

        return Rectangle.FromLTRB(minX, minY, maxX, maxY);
    }

    private static unsafe bool HasMeaningfulDifference(BitmapData previousData, BitmapData currentData, int startX, int startY, int width, int height)
    {
        var endX = Math.Min(startX + GridSize, width);
        var endY = Math.Min(startY + GridSize, height);

        for (var y = startY; y < endY; y += 4)
        {
            var previousRow = (byte*)previousData.Scan0 + (y * previousData.Stride);
            var currentRow = (byte*)currentData.Scan0 + (y * currentData.Stride);

            for (var x = startX; x < endX; x += 4)
            {
                var offset = x * 4;
                var blueDiff = Math.Abs(previousRow[offset] - currentRow[offset]);
                var greenDiff = Math.Abs(previousRow[offset + 1] - currentRow[offset + 1]);
                var redDiff = Math.Abs(previousRow[offset + 2] - currentRow[offset + 2]);

                if (blueDiff + greenDiff + redDiff >= PixelThreshold)
                {
                    return true;
                }
            }
        }

        return false;
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