using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using Ducz.LocalConnect.Core.Protocol;

namespace Ducz.LocalConnect.Core.Capture;

public sealed class FrameDiffEncoder : IDisposable
{
    private readonly ImageCodecInfo _jpegCodec;
    private readonly MemoryStream _encodeBuffer = new();

    private CaptureOptions _options;
    private Bitmap? _current;
    private Bitmap? _previous;
    private bool _hasPrevious;
    private long _lastFullFrameTicks;

    public FrameDiffEncoder(CaptureOptions? options = null)
    {
        _options = options ?? CaptureOptions.Default;
        _jpegCodec = ImageCodecInfo.GetImageEncoders().First(codec => codec.FormatID == ImageFormat.Jpeg.Guid);
    }

    public CaptureOptions Options => _options;

    public void SetOptions(CaptureOptions options)
    {
        _options = options;
        Reset();
    }

    public void Reset()
    {
        _hasPrevious = false;
    }

    public RemoteFrame? EncodeNext(IScreenSource source)
    {
        var bounds = source.Bounds;
        EnsureBuffers(bounds.Size);

        var current = _current!;
        source.Capture(current);

        var now = Stopwatch.GetTimestamp();
        var sendFull = !_hasPrevious || Stopwatch.GetElapsedTime(_lastFullFrameTicks, now) >= _options.FullFrameInterval;

        RemoteFrame? frame;
        if (sendFull)
        {
            _lastFullFrameTicks = now;
            var bytes = Encode(current, _options.LosslessFullFrames, _options.FullFrameQuality);
            frame = new RemoteFrame(RemoteFrameKind.Full, bounds.Width, bounds.Height, 0, 0, bounds.Width, bounds.Height, bytes);
        }
        else
        {
            var dirty = DetectDirtyBounds(_previous!, current, _options.GridSize, _options.PixelThreshold);
            if (dirty.IsEmpty)
            {
                frame = null;
            }
            else
            {
                using var patch = current.Clone(dirty, PixelFormat.Format32bppArgb);
                var bytes = Encode(patch, _options.LosslessDeltas, _options.DeltaFrameQuality);
                frame = new RemoteFrame(RemoteFrameKind.Delta, bounds.Width, bounds.Height, dirty.X, dirty.Y, dirty.Width, dirty.Height, bytes);
            }
        }

        (_current, _previous) = (_previous, _current);
        _hasPrevious = true;
        return frame;
    }

    public void Dispose()
    {
        _current?.Dispose();
        _previous?.Dispose();
        _encodeBuffer.Dispose();
    }

    private void EnsureBuffers(Size size)
    {
        if (_current is not null && _current.Size == size)
        {
            return;
        }

        _current?.Dispose();
        _previous?.Dispose();
        _current = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);
        _previous = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);
        Reset();
    }

    private byte[] Encode(Bitmap bitmap, bool lossless, long jpegQuality)
    {
        _encodeBuffer.SetLength(0);

        if (lossless)
        {
            // Drop the (always opaque) alpha channel: a 24-bit PNG is ~25% smaller and decodes faster.
            using var opaque = bitmap.Clone(new Rectangle(Point.Empty, bitmap.Size), PixelFormat.Format24bppRgb);
            opaque.Save(_encodeBuffer, ImageFormat.Png);
        }
        else
        {
            using var parameters = new EncoderParameters(1);
            parameters.Param[0] = new EncoderParameter(Encoder.Quality, jpegQuality);
            bitmap.Save(_encodeBuffer, _jpegCodec, parameters);
        }

        return _encodeBuffer.ToArray();
    }

    internal static unsafe Rectangle DetectDirtyBounds(Bitmap previous, Bitmap current, int gridSize, int pixelThreshold)
    {
        var width = current.Width;
        var height = current.Height;
        var bounds = new Rectangle(0, 0, width, height);

        var minX = int.MaxValue;
        var minY = int.MaxValue;
        var maxX = -1;
        var maxY = -1;

        var previousData = previous.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var currentData = current.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (var y = 0; y < height; y += gridSize)
            {
                for (var x = 0; x < width; x += gridSize)
                {
                    if (!CellChanged(previousData, currentData, x, y, gridSize, width, height, pixelThreshold))
                    {
                        continue;
                    }

                    minX = Math.Min(minX, x);
                    minY = Math.Min(minY, y);
                    maxX = Math.Max(maxX, Math.Min(x + gridSize, width));
                    maxY = Math.Max(maxY, Math.Min(y + gridSize, height));
                }
            }
        }
        finally
        {
            previous.UnlockBits(previousData);
            current.UnlockBits(currentData);
        }

        return maxX < 0 ? Rectangle.Empty : Rectangle.FromLTRB(minX, minY, maxX, maxY);
    }

    private static unsafe bool CellChanged(BitmapData previous, BitmapData current, int startX, int startY, int gridSize, int width, int height, int threshold)
    {
        var endX = Math.Min(startX + gridSize, width);
        var endY = Math.Min(startY + gridSize, height);

        for (var y = startY; y < endY; y += 4)
        {
            var previousRow = (byte*)previous.Scan0 + y * previous.Stride;
            var currentRow = (byte*)current.Scan0 + y * current.Stride;

            for (var x = startX; x < endX; x += 4)
            {
                var offset = x * 4;
                var diff = Math.Abs(previousRow[offset] - currentRow[offset])
                    + Math.Abs(previousRow[offset + 1] - currentRow[offset + 1])
                    + Math.Abs(previousRow[offset + 2] - currentRow[offset + 2]);

                if (diff >= threshold)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
