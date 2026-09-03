using Ducz.LocalConnect.Core.Transfer;

namespace Ducz.LocalConnect.Core.Tests.Transfer;

public class FileTransferReceiverTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ducz-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("report.pdf", "report.pdf")]
    [InlineData(@"..\..\Windows\evil.exe", "evil.exe")]
    [InlineData("../../etc/passwd", "passwd")]
    [InlineData(@"C:\Users\me\photo.jpg", "photo.jpg")]
    [InlineData("bad:name?.txt", "bad_name_.txt")]
    [InlineData("", "received-file")]
    [InlineData("   ", "received-file")]
    public void File_names_are_confined_to_the_target_directory(string incoming, string expected)
    {
        Assert.Equal(expected, FileTransferReceiver.SanitizeFileName(incoming));
    }

    [Fact]
    public async Task Chunks_are_reassembled_and_completion_is_reported()
    {
        using var receiver = new FileTransferReceiver(_directory);
        string? completedPath = null;
        receiver.Completed += path => completedPath = path;

        var content = Enumerable.Range(0, 50_000).Select(i => (byte)(i % 251)).ToArray();
        receiver.Begin("data.bin", content.Length);
        Assert.True(receiver.IsReceiving);

        await receiver.WriteAsync(content.AsMemory(0, 20_000), CancellationToken.None);
        await receiver.WriteAsync(content.AsMemory(20_000), CancellationToken.None);

        Assert.False(receiver.IsReceiving);
        Assert.NotNull(completedPath);
        Assert.Equal(Path.Combine(_directory, "data.bin"), completedPath);
        Assert.Equal(content, await File.ReadAllBytesAsync(completedPath));
    }

    [Fact]
    public void Existing_files_are_not_overwritten()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "notes.txt"), "keep me");

        using var receiver = new FileTransferReceiver(_directory);
        string? completedPath = null;
        receiver.Completed += path => completedPath = path;

        receiver.Begin("notes.txt", 0);

        Assert.Equal(Path.Combine(_directory, "notes (2).txt"), completedPath);
        Assert.Equal("keep me", File.ReadAllText(Path.Combine(_directory, "notes.txt")));
    }

    [Fact]
    public async Task Extra_bytes_beyond_the_declared_size_are_dropped()
    {
        using var receiver = new FileTransferReceiver(_directory);
        string? completedPath = null;
        receiver.Completed += path => completedPath = path;

        receiver.Begin("short.bin", 3);
        await receiver.WriteAsync(new byte[] { 1, 2, 3, 4, 5 }, CancellationToken.None);

        Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(completedPath!));
    }

    [Fact]
    public async Task Abort_removes_the_partial_file()
    {
        using var receiver = new FileTransferReceiver(_directory);
        receiver.Begin("partial.bin", 10);
        await receiver.WriteAsync(new byte[] { 1, 2 }, CancellationToken.None);

        receiver.Abort();

        Assert.False(receiver.IsReceiving);
        Assert.Empty(Directory.GetFiles(_directory));
    }
}
