using System.Linq;
using System.Security.Cryptography;

namespace Gpt2Image.Core.Storage;

public sealed class StoredMediaOutput
{
    public string FilePath { get; init; } = "";
    public string MimeType { get; init; } = "";
    public string Sha256 { get; init; } = "";
    public string OutputRole { get; init; } = "final";
    public long ByteLength { get; init; }
}

public sealed class LocalMediaStorage
{
    private readonly AppPaths _paths;
    private readonly IClock _clock;

    public LocalMediaStorage(AppPaths paths, IClock clock)
    {
        _paths = paths;
        _clock = clock;
    }

    public async Task<StoredMediaOutput> SaveVideoStreamAsync(
        string taskId,
        int index,
        Stream stream,
        string? contentType,
        CancellationToken cancellationToken)
    {
        _paths.EnsureDirectories();
        var now = _clock.UtcNow;
        var mimeType = NormalizeVideoMimeType(contentType);
        var extension = ExtensionForMimeType(mimeType);
        var directory = Path.Combine(_paths.VideosDirectory, now.ToString("yyyy"), now.ToString("MM"), now.ToString("dd"));
        Directory.CreateDirectory(directory);

        var filePath = ResolveUniqueFilePath(directory, $"{SanitizeFilePart(taskId)}_{index}", extension);
        await using var output = new FileStream(filePath, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 81920, useAsync: true);
        using var sha256 = SHA256.Create();
        var buffer = new byte[81920];
        long byteLength = 0;
        int read;
        while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            sha256.TransformBlock(buffer, 0, read, buffer, 0);
            byteLength += read;
        }

        sha256.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);

        return new StoredMediaOutput
        {
            FilePath = filePath,
            MimeType = mimeType,
            Sha256 = Convert.ToHexString(sha256.Hash ?? Array.Empty<byte>()),
            OutputRole = "final",
            ByteLength = byteLength
        };
    }

    private static string NormalizeVideoMimeType(string? contentType)
    {
        var normalized = (contentType ?? string.Empty).Split(';')[0].Trim().ToLowerInvariant();
        return normalized switch
        {
            "video/webm" => "video/webm",
            "video/quicktime" => "video/quicktime",
            "video/mp4" => "video/mp4",
            _ => "video/mp4"
        };
    }

    private static string ExtensionForMimeType(string mimeType) => mimeType switch
    {
        "video/webm" => "webm",
        "video/quicktime" => "mov",
        _ => "mp4"
    };

    private static string ResolveUniqueFilePath(string directory, string fileNameWithoutExtension, string extension)
    {
        var filePath = Path.Combine(directory, $"{fileNameWithoutExtension}.{extension}");
        if (!File.Exists(filePath))
        {
            return filePath;
        }

        for (var suffix = 1; suffix < 10_000; suffix++)
        {
            filePath = Path.Combine(directory, $"{fileNameWithoutExtension}_{suffix}.{extension}");
            if (!File.Exists(filePath))
            {
                return filePath;
            }
        }

        throw new IOException($"无法为视频输出创建唯一文件名：{fileNameWithoutExtension}.{extension}");
    }

    private static string SanitizeFilePart(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
    }
}
