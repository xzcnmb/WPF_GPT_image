using Gpt2Image.Core.Models;
using Gpt2Image.Core.Storage;
using Gpt2Image.Core.Storage.Repositories;
using Microsoft.Data.Sqlite;

namespace Gpt2Image.Tests.Storage;

public sealed class CodingAgentRepositoryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "gpt2image-coding-repo-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void TryCompleteRunIfNoPendingProposals_waits_until_all_proposals_are_resolved()
    {
        var database = CreateDatabase();
        var repository = new CodingAgentRepository(database);
        var now = DateTimeOffset.Parse("2026-06-08T00:00:00Z");
        repository.CreateRun(new CodingRunRecord
        {
            Id = "coding-1",
            WorkspacePath = _root,
            Title = "test",
            Goal = "goal",
            Status = CodingRunStatus.WaitingForApproval,
            Model = "gpt-4o-mini",
            CreatedAt = now,
            UpdatedAt = now
        });
        repository.AddFileChangeProposal(new CodingFileChangeProposalRecord
        {
            CodingRunId = "coding-1",
            RelativePath = "README.md",
            ChangeType = CodingFileChangeType.Replace,
            ProposedContent = "updated",
            DiffText = "diff",
            Summary = "summary",
            Status = CodingProposalStatus.Pending,
            CreatedAt = now
        });
        var commandId = repository.AddCommandProposal(new CodingCommandProposalRecord
        {
            CodingRunId = "coding-1",
            Command = "dotnet test",
            WorkingDirectory = ".",
            Reason = "verify",
            RiskLevel = CodingCommandRiskLevel.Low,
            Status = CodingProposalStatus.Pending,
            CreatedAt = now
        });

        Assert.False(repository.TryCompleteRunIfNoPendingProposals("coding-1", now.AddMinutes(1)));
        Assert.Equal(CodingRunStatus.WaitingForApproval, repository.GetRun("coding-1")?.Status);

        var fileId = repository.ListFileChangeProposals("coding-1").Single().Id;
        repository.UpdateFileChangeStatus(fileId, CodingProposalStatus.Applied, now.AddMinutes(2));
        Assert.False(repository.TryCompleteRunIfNoPendingProposals("coding-1", now.AddMinutes(3)));

        repository.UpdateCommandResult(commandId, CodingProposalStatus.Rejected, "", "用户拒绝运行该命令。", null, now.AddMinutes(4));
        Assert.True(repository.TryCompleteRunIfNoPendingProposals("coding-1", now.AddMinutes(5)));
        var completed = repository.GetRun("coding-1");
        Assert.Equal(CodingRunStatus.Completed, completed?.Status);
        Assert.Equal(now.AddMinutes(5), completed?.CompletedAt);
    }

    [Fact]
    public void MarkRunningRunsInterrupted_only_closes_open_runs()
    {
        var database = CreateDatabase();
        var repository = new CodingAgentRepository(database);
        var now = DateTimeOffset.Parse("2026-06-08T00:00:00Z");
        repository.CreateRun(new CodingRunRecord
        {
            Id = "open-run",
            WorkspacePath = _root,
            Title = "open",
            Goal = "goal",
            Status = CodingRunStatus.Running,
            Model = "gpt-4o-mini",
            CreatedAt = now,
            UpdatedAt = now
        });
        repository.CreateRun(new CodingRunRecord
        {
            Id = "waiting-run",
            WorkspacePath = _root,
            Title = "waiting",
            Goal = "goal",
            Status = CodingRunStatus.WaitingForApproval,
            Model = "gpt-4o-mini",
            CreatedAt = now,
            UpdatedAt = now
        });

        Assert.Equal(1, repository.MarkRunningRunsInterrupted(now.AddMinutes(1)));

        Assert.Equal(CodingRunStatus.Interrupted, repository.GetRun("open-run")?.Status);
        Assert.Equal(CodingRunStatus.WaitingForApproval, repository.GetRun("waiting-run")?.Status);
    }

    private SqliteDatabase CreateDatabase()
    {
        var database = new SqliteDatabase(AppPaths.CreateForRoot(_root));
        new SqliteSchemaInitializer(database).Initialize();
        return database;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
