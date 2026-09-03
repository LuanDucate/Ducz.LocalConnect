using System.Drawing;
using System.Drawing.Imaging;
using Ducz.LocalConnect.Core.Capture;
using Ducz.LocalConnect.Core.Protocol;

namespace Ducz.LocalConnect.Core.Tests.Capture;

public class FrameDiffEncoderTests
{
    private static readonly CaptureOptions NoPeriodicFullFrames = new() { FullFrameInterval = TimeSpan.FromHours(1) };

    private static Bitmap Solid(int width, int height, Color color)
    {
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(color);
        return bitmap;
    }

    private static bool IsJpeg(ReadOnlyMemory<byte> bytes) => bytes.Span[..2].SequenceEqual(new byte[] { 0xFF, 0xD8 });

    private static bool IsPng(ReadOnlyMemory<byte> bytes) => bytes.Span[..4].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47 });

    [Fact]
    public void Identical_frames_have_no_dirty_region()
    {
        using var a = Solid(128, 128, Color.White);
        using var b = Solid(128, 128, Color.White);

        Assert.Equal(Rectangle.Empty, FrameDiffEncoder.DetectDirtyBounds(a, b, gridSize: 16, pixelThreshold: 18));
    }

    [Fact]
    public void Changed_block_is_bounded_to_its_grid_cells()
    {
        using var a = Solid(128, 128, Color.White);
        using var b = Solid(128, 128, Color.White);
        using (var graphics = Graphics.FromImage(b))
        {
            graphics.FillRectangle(Brushes.Black, new Rectangle(40, 50, 20, 10));
        }

        var dirty = FrameDiffEncoder.DetectDirtyBounds(a, b, gridSize: 16, pixelThreshold: 18);

        // The change spans x 40..60 and y 50..60, i.e. grid cells [32,64) x [48,64).
        Assert.Equal(Rectangle.FromLTRB(32, 48, 64, 64), dirty);
    }

    [Fact]
    public void Noise_below_threshold_is_ignored()
    {
        using var a = Solid(64, 64, Color.FromArgb(100, 100, 100));
        using var b = Solid(64, 64, Color.FromArgb(103, 102, 101)); // sum of diffs = 6 < 18

        Assert.Equal(Rectangle.Empty, FrameDiffEncoder.DetectDirtyBounds(a, b, gridSize: 16, pixelThreshold: 18));
    }

    [Fact]
    public void First_frame_is_full_then_static_screen_sends_nothing()
    {
        using var source = new FakeScreenSource(96, 64, Color.CornflowerBlue);
        using var encoder = new FrameDiffEncoder(NoPeriodicFullFrames);

        var first = encoder.EncodeNext(source);
        Assert.NotNull(first);
        Assert.Equal(RemoteFrameKind.Full, first.Kind);
        Assert.Equal((96, 64), (first.DesktopWidth, first.DesktopHeight));
        Assert.True(IsJpeg(first.ImageBytes));

        Assert.Null(encoder.EncodeNext(source));
        Assert.Null(encoder.EncodeNext(source));
    }

    [Fact]
    public void Change_produces_delta_with_matching_rectangle()
    {
        using var source = new FakeScreenSource(96, 64, Color.White);
        using var encoder = new FrameDiffEncoder(NoPeriodicFullFrames);
        encoder.EncodeNext(source);

        source.Paint(new Rectangle(70, 20, 10, 10), Color.Red);
        var delta = encoder.EncodeNext(source);

        Assert.NotNull(delta);
        Assert.Equal(RemoteFrameKind.Delta, delta.Kind);
        Assert.Equal(Rectangle.FromLTRB(64, 16, 80, 32), new Rectangle(delta.X, delta.Y, delta.Width, delta.Height));
    }

    [Fact]
    public async Task Periodic_full_frame_is_sent_even_when_nothing_changed()
    {
        using var source = new FakeScreenSource(32, 32, Color.Gray);
        using var encoder = new FrameDiffEncoder(new CaptureOptions { FullFrameInterval = TimeSpan.FromMilliseconds(150) });

        Assert.Equal(RemoteFrameKind.Full, encoder.EncodeNext(source)!.Kind);
        Assert.Null(encoder.EncodeNext(source));

        await Task.Delay(250);
        Assert.Equal(RemoteFrameKind.Full, encoder.EncodeNext(source)!.Kind);
    }

    [Fact]
    public void Resolution_change_forces_a_full_frame()
    {
        using var small = new FakeScreenSource(32, 32, Color.Gray);
        using var large = new FakeScreenSource(64, 48, Color.Gray);
        using var encoder = new FrameDiffEncoder(NoPeriodicFullFrames);

        encoder.EncodeNext(small);
        Assert.Null(encoder.EncodeNext(small));

        var afterResize = encoder.EncodeNext(large);
        Assert.NotNull(afterResize);
        Assert.Equal(RemoteFrameKind.Full, afterResize.Kind);
        Assert.Equal((64, 48), (afterResize.DesktopWidth, afterResize.DesktopHeight));
    }

    [Fact]
    public void Sharp_preset_uses_png_for_deltas_and_jpeg_for_full_frames()
    {
        using var source = new FakeScreenSource(64, 64, Color.White);
        using var encoder = new FrameDiffEncoder(CaptureOptions.ForQuality(StreamQuality.Sharp) with { FullFrameInterval = TimeSpan.FromHours(1) });

        var full = encoder.EncodeNext(source)!;
        Assert.True(IsJpeg(full.ImageBytes));

        source.Paint(new Rectangle(0, 0, 8, 8), Color.Black);
        var delta = encoder.EncodeNext(source)!;
        Assert.Equal(RemoteFrameKind.Delta, delta.Kind);
        Assert.True(IsPng(delta.ImageBytes));
    }

    [Fact]
    public void Lossless_preset_uses_png_everywhere()
    {
        using var source = new FakeScreenSource(64, 64, Color.White);
        using var encoder = new FrameDiffEncoder(CaptureOptions.ForQuality(StreamQuality.Lossless));

        Assert.True(IsPng(encoder.EncodeNext(source)!.ImageBytes));
    }

    [Fact]
    public void Changing_options_forces_a_full_frame_in_the_new_format()
    {
        using var source = new FakeScreenSource(64, 64, Color.White);
        using var encoder = new FrameDiffEncoder(NoPeriodicFullFrames);
        encoder.EncodeNext(source);
        Assert.Null(encoder.EncodeNext(source));

        encoder.SetOptions(CaptureOptions.ForQuality(StreamQuality.Lossless));
        var frame = encoder.EncodeNext(source);

        Assert.NotNull(frame);
        Assert.Equal(RemoteFrameKind.Full, frame.Kind);
        Assert.True(IsPng(frame.ImageBytes));
    }

    private sealed class FakeScreenSource : IScreenSource
    {
        private readonly Bitmap _image;

        public FakeScreenSource(int width, int height, Color fill)
        {
            _image = Solid(width, height, fill);
            Bounds = new Rectangle(0, 0, width, height);
        }

        public Rectangle Bounds { get; }

        public void Paint(Rectangle area, Color color)
        {
            using var graphics = Graphics.FromImage(_image);
            using var brush = new SolidBrush(color);
            graphics.FillRectangle(brush, area);
        }

        public void Capture(Bitmap target)
        {
            using var graphics = Graphics.FromImage(target);
            graphics.DrawImageUnscaled(_image, 0, 0);
        }

        public void Dispose() => _image.Dispose();
    }
}
