using Ducz.LocalConnect.Core.Protocol;

namespace Ducz.LocalConnect.Core.Capture;

public sealed record CaptureOptions
{
    public static CaptureOptions Default { get; } = ForQuality(StreamQuality.Balanced);

    public int TargetFramesPerSecond { get; init; } = 30;

    public TimeSpan FullFrameInterval { get; init; } = TimeSpan.FromSeconds(3);

    public long FullFrameQuality { get; init; } = 65;

    public long DeltaFrameQuality { get; init; } = 80;

    public bool LosslessDeltas { get; init; }

    public bool LosslessFullFrames { get; init; }

    public int GridSize { get; init; } = 16;

    public int PixelThreshold { get; init; } = 18;

    public TimeSpan DisplayRefreshInterval { get; init; } = TimeSpan.FromSeconds(2);

    public TimeSpan FrameInterval => TimeSpan.FromSeconds(1d / Math.Max(1, TargetFramesPerSecond));

    public static CaptureOptions ForQuality(StreamQuality quality) => quality switch
    {
        StreamQuality.Sharp => new CaptureOptions
        {
            FullFrameQuality = 85,
            LosslessDeltas = true,
            FullFrameInterval = TimeSpan.FromSeconds(5),
        },
        StreamQuality.Lossless => new CaptureOptions
        {
            LosslessDeltas = true,
            LosslessFullFrames = true,
            FullFrameInterval = TimeSpan.FromSeconds(10),
        },
        _ => new CaptureOptions(),
    };
}
