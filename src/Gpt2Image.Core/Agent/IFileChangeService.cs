using Gpt2Image.Core.Models;

namespace Gpt2Image.Core.Agent;

public interface IFileChangeService
{
    FileChangeValidationResult Validate(string workspacePath, CodingFileChangeProposalDraft proposal);

    FileChangeApplyResult Apply(string workspacePath, CodingFileChangeProposalRecord proposal);

    string CreateDiff(string relativePath, string? beforeContent, string afterContent);
}
