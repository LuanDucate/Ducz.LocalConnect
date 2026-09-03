using System.Buffers.Binary;
using Ducz.LocalConnect.Core.Protocol;
using NAudio.Wave;

namespace Ducz.LocalConnect.Core.Audio;

public static class AudioProtocol
{
    public const int MaxChunkBytes = 1024 * 1024;

    public static ValueTask WriteWaveFormatAsync(Stream stream, WaveFormat format, CancellationToken cancellationToken)
    {
        var payload = new byte[16];
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0, 4), format.SampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4), format.BitsPerSample);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(8, 4), format.Channels);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(12, 4), format.Encoding == WaveFormatEncoding.IeeeFloat ? 1 : 0);
        return stream.WriteAsync(payload, cancellationToken);
    }

    public static async ValueTask<WaveFormat> ReadWaveFormatAsync(Stream stream, CancellationToken cancellationToken)
    {
        var payload = new byte[16];
        await stream.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);

        var sampleRate = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(0, 4));
        var bitsPerSample = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(4, 4));
        var channels = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(8, 4));
        var isFloat = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(12, 4)) == 1;

        if (sampleRate is < 8000 or > 384_000 || channels is < 1 or > 8 || bitsPerSample is not (8 or 16 or 24 or 32))
        {
            throw new ProtocolException("The audio format header is invalid.");
        }

        return isFloat
            ? WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels)
            : new WaveFormat(sampleRate, bitsPerSample, channels);
    }

    public static async ValueTask WriteChunkAsync(Stream stream, ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
    {
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, data.Length);
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(data, cancellationToken).ConfigureAwait(false);
    }

    public static async ValueTask<byte[]> ReadChunkAsync(Stream stream, CancellationToken cancellationToken)
    {
        var header = new byte[4];
        await stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length <= 0 || length > MaxChunkBytes)
        {
            throw new ProtocolException($"Audio chunk length {length} is out of range.");
        }

        var chunk = new byte[length];
        await stream.ReadExactlyAsync(chunk, cancellationToken).ConfigureAwait(false);
        return chunk;
    }
}
