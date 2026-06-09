using Gpt2Image.Core.Models;

namespace Gpt2Image.Core.Agent;

public sealed class FileChangeService : IFileChangeService
{
    private const int MaxProposedContentBytes = 600_000;
    private readonly ICodingWorkspaceService _workspace;

    public FileChangeService(ICodingWorkspaceService workspace)
    {
        _workspace = workspace;
    }

    public FileChangeValidationResult Validate(string workspacePath, CodingFileChangeProposalDraft proposal)
    {
        if (string.IsNullOrWhiteSpace(proposal.RelativePath))
        {
            return Deny("缺少文件路径。");
        }

        if (!_workspace.IsPathInsideWorkspace(workspacePath, proposal.RelativePath, out var fullPath))
        {
            return Deny("文件路径不在工作区内。");
        }

        if (_workspace.IsSensitivePath(proposal.RelativePath))
        {
            return Deny("出于安全考虑，默认不允许 AI 修改密钥、token、证书或 secrets 文件。");
        }

        if (!string.Equals(proposal.ChangeType, CodingFileChangeType.Create, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(proposal.ChangeType, CodingFileChangeType.Replace, StringComparison.OrdinalIgnoreCase))
        {
            return Deny("MVP 仅支持新增或替换文件，不支持删除/重命名。");
        }

        if (System.Text.Encoding.UTF8.GetByteCount(proposal.ProposedContent) > MaxProposedContentBytes)
        {
            return Deny("提议的文件内容过大，已拒绝。");
        }

        var exists = File.Exists(fullPath);
        if (string.Equals(proposal.ChangeType, CodingFileChangeType.Create, StringComparison.OrdinalIgnoreCase) && exists)
        {
            return Deny("新增文件已存在，不能覆盖。");
        }

        string? currentSha = null;
        string? beforeContent = null;
        if (exists)
        {
            currentSha = _workspace.ComputeSha256(fullPath);
            beforeContent = File.ReadAllText(fullPath);
        }

        if (string.Equals(proposal.ChangeType, CodingFileChangeType.Replace, StringComparison.OrdinalIgnoreCase))
        {
            if (!exists)
            {
                return Deny("替换目标文件不存在。");
            }

            if (string.IsNullOrWhiteSpace(proposal.OriginalSha256))
            {
                return Deny("替换文件必须携带原文件 SHA256，用于避免覆盖本地新改动。");
            }

            if (!string.Equals(currentSha, proposal.OriginalSha256, StringComparison.OrdinalIgnoreCase))
            {
                return new FileChangeValidationResult
                {
                    IsAllowed = false,
                    Message = "原文件 SHA256 已变化，请重新生成修改方案。",
                    CurrentSha256 = currentSha
                };
            }
        }

        return new FileChangeValidationResult
        {
            IsAllowed = true,
            Message = "文件变更可应用。",
            CurrentSha256 = currentSha,
            DiffText = CreateDiff(proposal.RelativePath, beforeContent, proposal.ProposedContent)
        };
    }

    public FileChangeApplyResult Apply(string workspacePath, CodingFileChangeProposalRecord proposal)
    {
        var validation = Validate(workspacePath, new CodingFileChangeProposalDraft
        {
            RelativePath = proposal.RelativePath,
            ChangeType = proposal.ChangeType,
            OriginalSha256 = proposal.OriginalSha256,
            ProposedContent = proposal.ProposedContent,
            Summary = proposal.Summary
        });
        if (!validation.IsAllowed)
        {
            return new FileChangeApplyResult { IsSuccess = false, Message = validation.Message };
        }

        if (!_workspace.IsPathInsideWorkspace(workspacePath, proposal.RelativePath, out var fullPath))
        {
            return new FileChangeApplyResult { IsSuccess = false, Message = "文件路径不在工作区内。" };
        }

        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(fullPath, proposal.ProposedContent);
        return new FileChangeApplyResult
        {
            IsSuccess = true,
            Message = "文件变更已应用。",
            FilePath = fullPath
        };
    }

    public string CreateDiff(string relativePath, string? beforeContent, string afterContent)
    {
        var beforeLines = SplitLines(beforeContent ?? string.Empty);
        var afterLines = SplitLines(afterContent);
        var lines = new List<string>
        {
            $"--- a/{relativePath}",
            $"+++ b/{relativePath}"
        };

        var max = Math.Max(beforeLines.Length, afterLines.Length);
        for (var i = 0; i < max; i++)
        {
            var before = i < beforeLines.Length ? beforeLines[i] : null;
            var after = i < afterLines.Length ? afterLines[i] : null;
            if (before == after && before is not null)
            {
                lines.Add($" {before}");
                continue;
            }

            if (before is not null)
            {
                lines.Add($"-{before}");
            }

            if (after is not null)
            {
                lines.Add($"+{after}");
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string[] SplitLines(string value)
    {
        return value.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
    }

    private static FileChangeValidationResult Deny(string message) => new()
    {
        IsAllowed = false,
        Message = message
    };
}
