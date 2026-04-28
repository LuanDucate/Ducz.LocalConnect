using System.Buffers.Binary;
using NAudio.Wave;

namespace Ducz.LocalConnect.App;

internal static class RemoteAudioProtocol
{
    public static async Task WriteWaveFormatAsync(Stream stream, WaveFormat format, CancellationToken cancellationToken)
    {
        var payload = new byte[16];
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0, 4), format.SampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4), format.BitsPerSample);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(8, 4), format.Channels);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(12, 4), format.Encoding == WaveFormatEncoding.IeeeFloat ? 1 : 0);

        await stream.WriteAsync(payload, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    public static async Task<WaveFormat> ReadWaveFormatAsync(Stream stream, CancellationToken cancellationToken)
    {
        var payload = await ReadExactAsync(stream, 16, cancellationToken);
        var sampleRate = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(0, 4));
        var bitsPerSample = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(4, 4));
        var channels = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(8, 4));
        var isFloat = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(12, 4)) == 1;

        return isFloat
            ? WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels)
            : new WaveFormat(sampleRate, bitsPerSample, channels);
    }

    public static async Task WriteAudioChunkAsync(Stream stream, ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
    {
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, data.Length);

        await stream.WriteAsync(header, cancellationToken);
        await stream.WriteAsync(data, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    public static async Task<byte[]> ReadAudioChunkAsync(Stream stream, CancellationToken cancellationToken)
    {
        var header = await ReadExactAsync(stream, 4, cancellationToken);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        return await ReadExactAsync(stream, length, cancellationToken);
    }

    private static async Task<byte[]> ReadExactAsync(Stream stream, int length, CancellationToken cancellationToken)
    {
        var buffer = new byte[length];
        var read = 0;

        while (read < length)
        {
            var currentRead = await stream.ReadAsync(buffer.AsMemory(read, length - read), cancellationToken);
            if (currentRead == 0)
            {
                throw new EndOfStreamException("The audio connection was closed.");
            }

            read += currentRead;
        }

        return buffer;
    }
}