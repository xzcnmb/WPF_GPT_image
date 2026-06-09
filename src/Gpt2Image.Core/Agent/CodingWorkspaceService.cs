using System.Security.Cryptography;
using System.Text;
using Gpt2Image.Core.Models;

namespace Gpt2Image.Core.Agent;

public sealed class CodingWorkspaceService : ICodingWorkspaceService
{
    private static readonly HashSet<string> SkippedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git",
        ".vs",
        ".idea",
        "bin",
        "obj",
        "node_modules",
        ".claude",
        ".pytest_cache",
        ".next",
        "dist",
        "build"
    };

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs",
        ".xaml",
        ".csproj",
        ".sln",
        ".props",
        ".targets",
        ".json",
        ".md",
        ".txt",
        ".xml",
        ".config",
        ".yml",
        ".yaml",
        ".js",
        ".ts",
        ".tsx",
        ".jsx",
        ".css",
        ".html",
        ".py",
        ".sql",
        ".sh",
        ".ps1"
    };

    public string NormalizeWorkspacePath(string workspacePath)
    {
        if (string.IsNullOrWhiteSpace(workspacePath))
        {
            throw new InvalidOperationException("请选择代码工作区目录。");
        }

        var fullPath = Path.GetFullPath(workspacePath.Trim());
        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException($"代码工作区不存在：{fullPath}");
        }

        return fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    public bool IsPathInsideWorkspace(string workspacePath, string relativePath, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            return false;
        }

        var root = NormalizeWorkspacePath(workspacePath);
        var candidate = Path.GetFullPath(Path.Combine(root, relativePath));
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(candidate, root, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        fullPath = candidate;
        return true;
    }

    public bool IsSensitivePath(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/');
        var fileName = Path.GetFileName(normalized);
        if (string.Equals(fileName, ".env", StringComparison.OrdinalIgnoreCase)
            || string.Equals(fileName, "secrets.json", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".pem", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".key", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".pfx", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return normalized.Contains("secret", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("password", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("token", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("credential", StringComparison.OrdinalIgnoreCase);
    }

    public CodingWorkspaceSnapshot BuildSnapshot(
        string workspacePath,
        int maxFiles = 24,
        int maxFileBytes = 24_000,
        int maxTotalBytes = 160_000)
    {
        var root = NormalizeWorkspacePath(workspacePath);
        var tree = new List<string>();
        var files = new List<CodingWorkspaceFile>();
        var skipped = new List<string>();
        var totalBytes = 0L;

        foreach (var filePath in EnumerateSafeFiles(root))
        {
            var relativePath = ToRelativePath(root, filePath);
            tree.Add(relativePath);

            if (files.Count >= maxFiles || totalBytes >= maxTotalBytes)
            {
                continue;
            }

            if (IsSensitivePath(relativePath))
            {
                skipped.Add($"{relativePath}（敏感文件）");
                continue;
            }

            var info = new FileInfo(filePath);
            if (info.Length > maxFileBytes)
            {
                skipped.Add($"{relativePath}（文件过大）");
                continue;
            }

            if (!LooksLikeTextFile(filePath))
            {
                skipped.Add($"{relativePath}（非文本文件）");
                continue;
            }

            try
            {
                var content = File.ReadAllText(filePath, Encoding.UTF8);
                totalBytes += Encoding.UTF8.GetByteCount(content);
                files.Add(new CodingWorkspaceFile
                {
                    RelativePath = relativePath,
                    Sha256 = ComputeSha256(filePath),
                    ByteLength = info.Length,
                    Content = content
                });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
            {
                skipped.Add($"{relativePath}（读取失败：{ex.Message}）");
            }
        }

        return new CodingWorkspaceSnapshot
        {
            WorkspacePath = root,
            FileTree = tree.Take(400).ToList(),
            Files = files,
            SkippedFiles = skipped
        };
    }

    public IReadOnlyList<string> ListSafeFileTree(string workspacePath, int maxEntries = 400)
    {
        var root = NormalizeWorkspacePath(workspacePath);
        return EnumerateSafeFiles(root)
            .Select(filePath => ToRelativePath(root, filePath))
            .Where(relativePath => !IsSensitivePath(relativePath))
            .Take(Math.Max(1, maxEntries))
            .ToList();
    }

    public CodingWorkspaceFile? ReadTextFile(string workspacePath, string relativePath, int maxBytes = 80_000)
    {
        if (!IsPathInsideWorkspace(workspacePath, relativePath, out var fullPath) || IsSensitivePath(relativePath))
        {
            return null;
        }

        if (!File.Exists(fullPath) || !LooksLikeTextFile(fullPath))
        {
            return null;
        }

        var info = new FileInfo(fullPath);
        if (info.Length > maxBytes)
        {
            return null;
        }

        try
        {
            return new CodingWorkspaceFile
            {
                RelativePath = ToRelativePath(NormalizeWorkspacePath(workspacePath), fullPath),
                Sha256 = ComputeSha256(fullPath),
                ByteLength = info.Length,
                Content = File.ReadAllText(fullPath, Encoding.UTF8)
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            return null;
        }
    }

    public IReadOnlyList<CodingWorkspaceSearchResult> SearchText(string workspacePath, string query, int maxResults = 80, int maxFileBytes = 240_000)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<CodingWorkspaceSearchResult>();
        }

        var root = NormalizeWorkspacePath(workspacePath);
        var results = new List<CodingWorkspaceSearchResult>();
        var needle = query.Trim();
        foreach (var filePath in EnumerateSafeFiles(root))
        {
            if (results.Count >= maxResults)
            {
                break;
            }

            var relativePath = ToRelativePath(root, filePath);
            if (IsSensitivePath(relativePath) || !LooksLikeTextFile(filePath))
            {
                continue;
            }

            var info = new FileInfo(filePath);
            if (info.Length > maxFileBytes)
            {
                continue;
            }

            try
            {
                var lineNumber = 0;
                foreach (var line in File.ReadLines(filePath, Encoding.UTF8))
                {
                    lineNumber++;
                    if (!line.Contains(needle, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    results.Add(new CodingWorkspaceSearchResult
                    {
                        RelativePath = relativePath,
                        LineNumber = lineNumber,
                        LineText = line.Trim()
                    });
                    if (results.Count >= maxResults)
                    {
                        break;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
            {
            }
        }

        return results;
    }

    public string ComputeSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static IEnumerable<string> EnumerateSafeFiles(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            IEnumerable<string> childDirectories;
            IEnumerable<string> files;
            try
            {
                childDirectories = Directory.EnumerateDirectories(directory).ToArray();
                files = Directory.EnumerateFiles(directory).ToArray();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var child in childDirectories.OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
            {
                if (!SkippedDirectories.Contains(Path.GetFileName(child)))
                {
                    pending.Push(child);
                }
            }

            foreach (var file in files.OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
            {
                yield return file;
            }
        }
    }

    private static bool LooksLikeTextFile(string filePath)
    {
        if (!TextExtensions.Contains(Path.GetExtension(filePath)))
        {
            return false;
        }

        Span<byte> buffer = stackalloc byte[512];
        using var stream = File.OpenRead(filePath);
        var read = stream.Read(buffer);
        return !buffer[..read].Contains((byte)0);
    }

    private static string ToRelativePath(string root, string filePath)
    {
        return Path.GetRelativePath(root, filePath).Replace('\\', '/');
    }
}
