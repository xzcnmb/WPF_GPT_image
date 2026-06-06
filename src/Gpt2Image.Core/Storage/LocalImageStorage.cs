using System.Security.Cryptography;
using Gpt2Image.Core.Models;

namespace Gpt2Image.Core.Storage;

public enum ImageOutputRole
{
    Final,
    AgentDraft,
    Choice,
    Partial
}

public sealed class StoredImageOutput
{
    public string FilePath { get; init; } = "";
    public string MimeType { get; init; } = "";
    public string Sha256 { get; init; } = "";
    public string OutputRole { get; init; } = "";
    public long ByteLength { get; init; }
}

public sealed class StoredInputAsset
{
    public string FilePath { get; init; } = "";
    public string MimeType { get; init; } = "";
    public string Sha256 { get; init; } = "";
    public long ByteLength { get; init; }
}

public sealed class LocalImageStorage
{
    private readonly AppPaths _paths;
    private readonly IClock _clock;

    public LocalImageStorage(AppPaths paths, IClock clock)
    {
        _paths = paths;
        _clock = clock;
    }

    public StoredImageOutput SaveBase64Image(
        string taskId,
        int index,
        string base64,
        string outputFormat,
        ImageOutputRole role)
    {
        _paths.EnsureDirectories();
        var now = _clock.UtcNow;
        var extension = NormalizeExtension(outputFormat);
        var directory = Path.Combine(_paths.ImagesDirectory, now.ToString("yyyy"), now.ToString("MM"), now.ToString("dd"));
        Directory.CreateDirectory(directory);

        var filePath = Path.Combine(directory, $"{SanitizeFilePart(taskId)}_{index}.{extension}");
        var bytes = Convert.FromBase64String(base64);
        File.WriteAllBytes(filePath, bytes);

        return new StoredImageOutput
        {
            FilePath = filePath,
            MimeType = MimeTypeFor(extension),
            Sha256 = Convert.ToHexString(SHA256.HashData(bytes)),
            OutputRole = RoleName(role),
            ByteLength = bytes.LongLength
        };
    }

    public GeneratedImageOutput SaveGeneratedImage(string taskId, int index, string base64, string outputFormat, ImageOutputRole role)
    {
        var saved = SaveBase64Image(taskId, index, base64, outputFormat, role);
        return new GeneratedImageOutput
        {
            FilePath = saved.FilePath,
            Base64 = base64,
            Index = index,
            OutputRole = saved.OutputRole
        };
    }

    public StoredInputAsset SaveInputAsset(string sourcePath)
    {
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("输入图片不存在。", sourcePath);
        }

        _paths.EnsureDirectories();
        var now = _clock.UtcNow;
        var extension = NormalizeExtension(Path.GetExtension(sourcePath));
        var directory = Path.Combine(_paths.ImagesDirectory, "inputs", now.ToString("yyyy"), now.ToString("MM"), now.ToString("dd"));
        Directory.CreateDirectory(directory);

        var bytes = File.ReadAllBytes(sourcePath);
        var sha256 = Convert.ToHexString(SHA256.HashData(bytes));
        var fileName = $"{now:HHmmss}_{Guid.NewGuid():N}.{extension}";
        var filePath = Path.Combine(directory, fileName);
        File.WriteAllBytes(filePath, bytes);

        return new StoredInputAsset
        {
            FilePath = filePath,
            MimeType = MimeTypeFor(extension),
            Sha256 = sha256,
            ByteLength = bytes.LongLength
        };
    }

    private static string NormalizeExtension(string value)
    {
        var normalized = value.Trim().TrimStart('.').ToLowerInvariant();
        return normalized is "jpg" ? "jpeg" : normalized switch
        {
            "png" or "jpeg" or "webp" => normalized,
            _ => "png"
        };
    }

    private static string MimeTypeFor(string extension) => extension switch
    {
        "jpeg" => "image/jpeg",
        "webp" => "image/webp",
        _ => "image/png"
    };

    private static string RoleName(ImageOutputRole role) => role switch
    {
        ImageOutputRole.AgentDraft => "agent_draft",
        ImageOutputRole.Choice => "choice",
        ImageOutputRole.Partial => "partial",
        _ => "final"
    };

    private static string SanitizeFilePart(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
    }
}
