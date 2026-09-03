namespace Ducz.LocalConnect.Core.Transfer;

public sealed class FileTransferReceiver : IDisposable
{
    private readonly string _directory;

    private FileStream? _file;
    private string? _path;
    private long _expectedBytes;
    private long _receivedBytes;

    public FileTransferReceiver(string directory)
    {
        _directory = directory;
    }

    public event Action<string, long>? Started;
    public event Action<long, long>? Progress;
    public event Action<string>? Completed;

    public bool IsReceiving => _file is not null;

    public void Begin(string fileName, long size)
    {
        Abort();

        Directory.CreateDirectory(_directory);
        _path = UniquePath(_directory, SanitizeFileName(fileName));
        _expectedBytes = size;
        _receivedBytes = 0;
        _file = new FileStream(_path, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 64 * 1024, useAsync: true);
        Started?.Invoke(Path.GetFileName(_path), size);

        if (size == 0)
        {
            Finish();
        }
    }

    public async ValueTask WriteAsync(ReadOnlyMemory<byte> chunk, CancellationToken cancellationToken)
    {
        var file = _file;
        if (file is null)
        {
            return; // chunk without metadata: ignore rather than crash the session
        }

        var remaining = _expectedBytes - _receivedBytes;
        if (chunk.Length > remaining)
        {
            chunk = chunk[..(int)remaining];
        }

        await file.WriteAsync(chunk, cancellationToken).ConfigureAwait(false);
        _receivedBytes += chunk.Length;
        Progress?.Invoke(_receivedBytes, _expectedBytes);

        if (_receivedBytes >= _expectedBytes)
        {
            await file.FlushAsync(cancellationToken).ConfigureAwait(false);
            Finish();
        }
    }

    public void Abort()
    {
        var file = _file;
        var path = _path;
        _file = null;
        _path = null;
        if (file is null)
        {
            return;
        }

        file.Dispose();
        try
        {
            if (path is not null)
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
    }

    public void Dispose() => Abort();

    private void Finish()
    {
        var file = _file;
        var path = _path;
        _file = null;
        _path = null;
        file?.Dispose();
        if (path is not null)
        {
            Completed?.Invoke(path);
        }
    }

    internal static string SanitizeFileName(string fileName)
    {
        var name = Path.GetFileName(fileName.Replace('\\', '/'));
        if (string.IsNullOrWhiteSpace(name))
        {
            return "received-file";
        }

        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (Array.IndexOf(invalid, chars[i]) >= 0)
            {
                chars[i] = '_';
            }
        }

        return new string(chars).Trim();
    }

    private static string UniquePath(string directory, string fileName)
    {
        var candidate = Path.Combine(directory, fileName);
        if (!File.Exists(candidate))
        {
            return candidate;
        }

        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var i = 2; ; i++)
        {
            candidate = Path.Combine(directory, $"{stem} ({i}){extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }
    }
}
