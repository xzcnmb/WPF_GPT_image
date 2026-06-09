using Gpt2Image.Core.Models;

namespace Gpt2Image.Core.Agent;

public interface ICodingWorkspaceService
{
    string NormalizeWorkspacePath(string workspacePath);

    bool IsPathInsideWorkspace(string workspacePath, string relativePath, out string fullPath);

    bool IsSensitivePath(string relativePath);

    CodingWorkspaceSnapshot BuildSnapshot(
        string workspacePath,
        int maxFiles = 24,
        int maxFileBytes = 24_000,
        int maxTotalBytes = 160_000);

    IReadOnlyList<string> ListSafeFileTree(string workspacePath, int maxEntries = 400);

    CodingWorkspaceFile? ReadTextFile(string workspacePath, string relativePath, int maxBytes = 80_000);

    IReadOnlyList<CodingWorkspaceSearchResult> SearchText(string workspacePath, string query, int maxResults = 80, int maxFileBytes = 240_000);

    string ComputeSha256(string filePath);
}
