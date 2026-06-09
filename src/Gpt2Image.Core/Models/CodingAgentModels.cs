namespace Gpt2Image.Core.Models;

public static class CodingRunStatus
{
    public const string Pending = "pending";
    public const string Running = "running";
    public const string WaitingForApproval = "waiting_for_approval";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Canceled = "canceled";
    public const string Interrupted = "interrupted";
}

public static class CodingEventKind
{
    public const string UserMessage = "user_message";
    public const string AssistantMessage = "assistant_message";
    public const string Plan = "plan";
    public const string FileRead = "file_read";
    public const string PatchProposed = "patch_proposed";
    public const string PatchApplied = "patch_applied";
    public const string CommandProposed = "command_proposed";
    public const string CommandStarted = "command_started";
    public const string CommandCompleted = "command_completed";
    public const string ApprovalRequired = "approval_required";
    public const string Error = "error";
}

public static class CodingProposalStatus
{
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Applied = "applied";
    public const string Failed = "failed";
}

public static class CodingFileChangeType
{
    public const string Create = "create";
    public const string Replace = "replace";
}

public static class CodingCommandRiskLevel
{
    public const string Low = "low";
    public const string Medium = "medium";
    public const string High = "high";
    public const string Blocked = "blocked";
}

public static class CodingSessionMode
{
    public const string Chat = "chat";
    public const string Clarify = "clarify";
    public const string Cowork = "cowork";
    public const string Code = "code";
    public const string AutonomousCodingPipeline = "acp";
}

public sealed class CodingRunRecord
{
    public string Id { get; init; } = "";
    public string WorkspacePath { get; init; } = "";
    public string Title { get; init; } = "";
    public string Goal { get; init; } = "";
    public string Status { get; init; } = CodingRunStatus.Pending;
    public string? BackendProfileId { get; init; }
    public string Model { get; init; } = "";
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
}

public sealed class CodingRunEventRecord
{
    public long Id { get; init; }
    public string CodingRunId { get; init; } = "";
    public int Sequence { get; init; }
    public string Kind { get; init; } = CodingEventKind.AssistantMessage;
    public string Title { get; init; } = "";
    public string? Detail { get; init; }
    public string Status { get; init; } = CodingProposalStatus.Applied;
    public string? RawJson { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

public sealed class CodingFileChangeProposalRecord
{
    public long Id { get; init; }
    public string CodingRunId { get; init; } = "";
    public long? EventId { get; init; }
    public string RelativePath { get; init; } = "";
    public string ChangeType { get; init; } = CodingFileChangeType.Replace;
    public string? OriginalSha256 { get; init; }
    public string ProposedContent { get; init; } = "";
    public string DiffText { get; init; } = "";
    public string Summary { get; init; } = "";
    public string Status { get; init; } = CodingProposalStatus.Pending;
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? AppliedAt { get; init; }
}

public sealed class CodingCommandProposalRecord
{
    public long Id { get; init; }
    public string CodingRunId { get; init; } = "";
    public long? EventId { get; init; }
    public string Command { get; init; } = "";
    public string WorkingDirectory { get; init; } = "";
    public string Reason { get; init; } = "";
    public string RiskLevel { get; init; } = CodingCommandRiskLevel.Low;
    public string Status { get; init; } = CodingProposalStatus.Pending;
    public string? Stdout { get; init; }
    public string? Stderr { get; init; }
    public int? ExitCode { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
}

public sealed class CodingWorkspaceFile
{
    public string RelativePath { get; init; } = "";
    public string Sha256 { get; init; } = "";
    public long ByteLength { get; init; }
    public string Content { get; init; } = "";
}

public sealed class CodingWorkspaceSnapshot
{
    public string WorkspacePath { get; init; } = "";
    public IReadOnlyList<string> FileTree { get; init; } = Array.Empty<string>();
    public IReadOnlyList<CodingWorkspaceFile> Files { get; init; } = Array.Empty<CodingWorkspaceFile>();
    public IReadOnlyList<string> SkippedFiles { get; init; } = Array.Empty<string>();
}

public sealed class CodingWorkspaceSearchResult
{
    public string RelativePath { get; init; } = "";
    public int LineNumber { get; init; }
    public string LineText { get; init; } = "";
}

public sealed class CodingAgentRequest
{
    public string Goal { get; init; } = "";
    public string WorkspacePath { get; init; } = "";
    public string SessionMode { get; init; } = CodingSessionMode.Cowork;
    public CodingWorkspaceSnapshot WorkspaceSnapshot { get; init; } = new();
    public int MaxFileChanges { get; init; } = 8;
    public int MaxCommands { get; init; } = 4;
}

public sealed class CodingAgentResponse
{
    public string Message { get; init; } = "";
    public IReadOnlyList<string> Plan { get; init; } = Array.Empty<string>();
    public IReadOnlyList<CodingFileChangeProposalDraft> FileChanges { get; init; } = Array.Empty<CodingFileChangeProposalDraft>();
    public IReadOnlyList<CodingCommandProposalDraft> Commands { get; init; } = Array.Empty<CodingCommandProposalDraft>();
    public string RawJson { get; init; } = "";
    public string? Error { get; init; }
}

public sealed class CodingFileChangeProposalDraft
{
    public string RelativePath { get; init; } = "";
    public string ChangeType { get; init; } = CodingFileChangeType.Replace;
    public string? OriginalSha256 { get; init; }
    public string ProposedContent { get; init; } = "";
    public string Summary { get; init; } = "";
}

public sealed class CodingCommandProposalDraft
{
    public string Command { get; init; } = "";
    public string WorkingDirectory { get; init; } = "";
    public string Reason { get; init; } = "";
}

public sealed class CodingUsage
{
    public int? PromptTokens { get; init; }
    public int? CompletionTokens { get; init; }
    public int? TotalTokens { get; init; }
}

public sealed class FileChangeValidationResult
{
    public bool IsAllowed { get; init; }
    public string Message { get; init; } = "";
    public string DiffText { get; init; } = "";
    public string? CurrentSha256 { get; init; }
}

public sealed class FileChangeApplyResult
{
    public bool IsSuccess { get; init; }
    public string Message { get; init; } = "";
    public string? FilePath { get; init; }
}

public sealed class CommandSafetyResult
{
    public bool IsAllowed { get; init; }
    public string RiskLevel { get; init; } = CodingCommandRiskLevel.Blocked;
    public string Message { get; init; } = "";
}

public sealed class ProcessRunResult
{
    public string Command { get; init; } = "";
    public string WorkingDirectory { get; init; } = "";
    public string Stdout { get; init; } = "";
    public string Stderr { get; init; } = "";
    public int? ExitCode { get; init; }
    public bool TimedOut { get; init; }
}
