using Gpt2Image.Core.Storage;

namespace Gpt2Image.Tests.Storage;

public sealed class FileStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "gpt2image-file-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void SaveBase64Image_writes_dated_output_and_returns_sha256()
    {
        var paths = AppPaths.CreateForRoot(_root);
        var storage = new LocalImageStorage(paths, new FixedClock(new DateTimeOffset(2026, 6, 6, 12, 0, 0, TimeSpan.Zero)));
        var base64 = Convert.ToBase64String(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 1, 2, 3 });

        var output = storage.SaveBase64Image("task-1", 2, base64, "png", ImageOutputRole.Final);

        Assert.True(File.Exists(output.FilePath));
        Assert.EndsWith(Path.Combine("images", "2026", "06", "06", "task-1_2.png"), output.FilePath);
        Assert.Equal("image/png", output.MimeType);
        Assert.Equal("final", output.OutputRole);
        Assert.Equal("7F47B756761A46E6D4A4D96F0D8A4448F8449235009D1F3AD1493F5C773C19E8", output.Sha256);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class FixedClock : IClock
    {
        private readonly DateTimeOffset _now;

        public FixedClock(DateTimeOffset now)
        {
            _now = now;
        }

        public DateTimeOffset UtcNow => _now;
    }
}
