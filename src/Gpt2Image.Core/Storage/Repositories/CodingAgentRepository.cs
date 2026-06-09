using Dapper;
using Gpt2Image.Core.Models;

namespace Gpt2Image.Core.Storage.Repositories;

public sealed class CodingAgentRepository
{
    private readonly SqliteDatabase _database;

    public CodingAgentRepository(SqliteDatabase database)
    {
        _database = database;
    }

    public void CreateRun(CodingRunRecord run)
    {
        using var connection = _database.OpenConnection();
        connection.Execute(
            @"
            insert into coding_runs (
                id, workspace_path, title, goal, status, backend_profile_id, model, created_at, updated_at, completed_at
            ) values (
                @Id, @WorkspacePath, @Title, @Goal, @Status, @BackendProfileId, @Model, @CreatedAt, @UpdatedAt, @CompletedAt
            )",
            new
            {
                run.Id,
                run.WorkspacePath,
                run.Title,
                run.Goal,
                run.Status,
                run.BackendProfileId,
                run.Model,
                CreatedAt = run.CreatedAt.ToString("O"),
                UpdatedAt = run.UpdatedAt.ToString("O"),
                CompletedAt = run.CompletedAt?.ToString("O")
            });
    }

    public void UpdateRunStatus(string runId, string status, DateTimeOffset updatedAt, DateTimeOffset? completedAt = null)
    {
        using var connection = _database.OpenConnection();
        connection.Execute(
            @"
            update coding_runs
            set status = @Status,
                updated_at = @UpdatedAt,
                completed_at = @CompletedAt
            where id = @RunId",
            new
            {
                RunId = runId,
                Status = status,
                UpdatedAt = updatedAt.ToString("O"),
                CompletedAt = completedAt?.ToString("O")
            });
    }

    public int MarkRunningRunsInterrupted(DateTimeOffset updatedAt)
    {
        using var connection = _database.OpenConnection();
        return connection.Execute(
            @"
            update coding_runs
            set status = @Status,
                updated_at = @UpdatedAt,
                completed_at = coalesce(completed_at, @UpdatedAt)
            where status in ('pending', 'running')",
            new
            {
                Status = CodingRunStatus.Interrupted,
                UpdatedAt = updatedAt.ToString("O")
            });
    }

    public bool TryCompleteRunIfNoPendingProposals(string runId, DateTimeOffset completedAt)
    {
        using var connection = _database.OpenConnection();
        var pendingFileChanges = connection.ExecuteScalar<int>(
            "select count(1) from coding_file_change_proposals where coding_run_id = @RunId and status = @Status",
            new { RunId = runId, Status = CodingProposalStatus.Pending });
        var pendingCommands = connection.ExecuteScalar<int>(
            "select count(1) from coding_command_proposals where coding_run_id = @RunId and status = @Status",
            new { RunId = runId, Status = CodingProposalStatus.Pending });
        if (pendingFileChanges + pendingCommands > 0)
        {
            return false;
        }

        var updated = connection.Execute(
            @"
            update coding_runs
            set status = @CompletedStatus,
                updated_at = @UpdatedAt,
                completed_at = @UpdatedAt
            where id = @RunId
              and status in (@WaitingStatus, @RunningStatus)",
            new
            {
                RunId = runId,
                CompletedStatus = CodingRunStatus.Completed,
                WaitingStatus = CodingRunStatus.WaitingForApproval,
                RunningStatus = CodingRunStatus.Running,
                UpdatedAt = completedAt.ToString("O")
            });
        return updated > 0;
    }

    public long AddEvent(CodingRunEventRecord item)
    {
        using var connection = _database.OpenConnection();
        var sequence = item.Sequence > 0
            ? item.Sequence
            : connection.ExecuteScalar<int?>("select max(sequence) from coding_events where coding_run_id = @RunId", new { RunId = item.CodingRunId }) is { } maxSequence
                ? maxSequence + 1
                : 1;
        connection.Execute(
            @"
            insert into coding_events (
                coding_run_id, sequence, kind, title, detail, status, raw_json, created_at
            ) values (
                @CodingRunId, @Sequence, @Kind, @Title, @Detail, @Status, @RawJson, @CreatedAt
            )",
            new
            {
                item.CodingRunId,
                Sequence = sequence,
                item.Kind,
                item.Title,
                item.Detail,
                item.Status,
                item.RawJson,
                CreatedAt = item.CreatedAt.ToString("O")
            });
        connection.Execute("update coding_runs set updated_at = @UpdatedAt where id = @RunId", new { RunId = item.CodingRunId, UpdatedAt = item.CreatedAt.ToString("O") });
        return connection.ExecuteScalar<long>("select last_insert_rowid()");
    }

    public long AddFileChangeProposal(CodingFileChangeProposalRecord proposal)
    {
        using var connection = _database.OpenConnection();
        connection.Execute(
            @"
            insert into coding_file_change_proposals (
                coding_run_id, event_id, relative_path, change_type, original_sha256, proposed_content,
                diff_text, summary, status, created_at, applied_at
            ) values (
                @CodingRunId, @EventId, @RelativePath, @ChangeType, @OriginalSha256, @ProposedContent,
                @DiffText, @Summary, @Status, @CreatedAt, @AppliedAt
            )",
            new
            {
                proposal.CodingRunId,
                proposal.EventId,
                proposal.RelativePath,
                proposal.ChangeType,
                proposal.OriginalSha256,
                proposal.ProposedContent,
                proposal.DiffText,
                proposal.Summary,
                proposal.Status,
                CreatedAt = proposal.CreatedAt.ToString("O"),
                AppliedAt = proposal.AppliedAt?.ToString("O")
            });
        return connection.ExecuteScalar<long>("select last_insert_rowid()");
    }

    public long AddCommandProposal(CodingCommandProposalRecord proposal)
    {
        using var connection = _database.OpenConnection();
        connection.Execute(
            @"
            insert into coding_command_proposals (
                coding_run_id, event_id, command, working_directory, reason, risk_level, status,
                stdout, stderr, exit_code, created_at, completed_at
            ) values (
                @CodingRunId, @EventId, @Command, @WorkingDirectory, @Reason, @RiskLevel, @Status,
                @Stdout, @Stderr, @ExitCode, @CreatedAt, @CompletedAt
            )",
            new
            {
                proposal.CodingRunId,
                proposal.EventId,
                proposal.Command,
                proposal.WorkingDirectory,
                proposal.Reason,
                proposal.RiskLevel,
                proposal.Status,
                proposal.Stdout,
                proposal.Stderr,
                proposal.ExitCode,
                CreatedAt = proposal.CreatedAt.ToString("O"),
                CompletedAt = proposal.CompletedAt?.ToString("O")
            });
        return connection.ExecuteScalar<long>("select last_insert_rowid()");
    }

    public void UpdateFileChangeStatus(long proposalId, string status, DateTimeOffset? appliedAt = null)
    {
        using var connection = _database.OpenConnection();
        connection.Execute(
            @"
            update coding_file_change_proposals
            set status = @Status,
                applied_at = @AppliedAt
            where id = @ProposalId",
            new { ProposalId = proposalId, Status = status, AppliedAt = appliedAt?.ToString("O") });
    }

    public void UpdateCommandResult(long proposalId, string status, string stdout, string stderr, int? exitCode, DateTimeOffset completedAt)
    {
        using var connection = _database.OpenConnection();
        connection.Execute(
            @"
            update coding_command_proposals
            set status = @Status,
                stdout = @Stdout,
                stderr = @Stderr,
                exit_code = @ExitCode,
                completed_at = @CompletedAt
            where id = @ProposalId",
            new
            {
                ProposalId = proposalId,
                Status = status,
                Stdout = stdout,
                Stderr = stderr,
                ExitCode = exitCode,
                CompletedAt = completedAt.ToString("O")
            });
    }

    public IReadOnlyList<CodingRunRecord> ListRecentRuns(int limit = 50)
    {
        using var connection = _database.OpenConnection();
        return connection.Query<CodingRunRow>(
                @"
                select id, workspace_path as WorkspacePath, title, goal, status, backend_profile_id as BackendProfileId,
                       model, created_at as CreatedAt, updated_at as UpdatedAt, completed_at as CompletedAt
                from coding_runs
                order by updated_at desc
                limit @Limit",
                new { Limit = Math.Max(1, limit) })
            .Select(ToRunRecord)
            .ToList();
    }

    public CodingRunRecord? GetRun(string runId)
    {
        using var connection = _database.OpenConnection();
        var row = connection.QuerySingleOrDefault<CodingRunRow>(
            @"
            select id, workspace_path as WorkspacePath, title, goal, status, backend_profile_id as BackendProfileId,
                   model, created_at as CreatedAt, updated_at as UpdatedAt, completed_at as CompletedAt
            from coding_runs
            where id = @RunId",
            new { RunId = runId });
        return row is null ? null : ToRunRecord(row);
    }

    public IReadOnlyList<CodingRunEventRecord> ListEvents(string runId)
    {
        using var connection = _database.OpenConnection();
        return connection.Query<CodingEventRow>(
                @"
                select id, coding_run_id as CodingRunId, sequence, kind, title, detail, status, raw_json as RawJson, created_at as CreatedAt
                from coding_events
                where coding_run_id = @RunId
                order by sequence, id",
                new { RunId = runId })
            .Select(ToEventRecord)
            .ToList();
    }

    public IReadOnlyList<CodingFileChangeProposalRecord> ListFileChangeProposals(string runId)
    {
        using var connection = _database.OpenConnection();
        return connection.Query<CodingFileChangeRow>(
                @"
                select id, coding_run_id as CodingRunId, event_id as EventId, relative_path as RelativePath,
                       change_type as ChangeType, original_sha256 as OriginalSha256, proposed_content as ProposedContent,
                       diff_text as DiffText, summary, status, created_at as CreatedAt, applied_at as AppliedAt
                from coding_file_change_proposals
                where coding_run_id = @RunId
                order by id",
                new { RunId = runId })
            .Select(ToFileChangeRecord)
            .ToList();
    }

    public IReadOnlyList<CodingCommandProposalRecord> ListCommandProposals(string runId)
    {
        using var connection = _database.OpenConnection();
        return connection.Query<CodingCommandRow>(
                @"
                select id, coding_run_id as CodingRunId, event_id as EventId, command, working_directory as WorkingDirectory,
                       reason, risk_level as RiskLevel, status, stdout, stderr, exit_code as ExitCode,
                       created_at as CreatedAt, completed_at as CompletedAt
                from coding_command_proposals
                where coding_run_id = @RunId
                order by id",
                new { RunId = runId })
            .Select(ToCommandRecord)
            .ToList();
    }

    private static CodingRunRecord ToRunRecord(CodingRunRow row) => new()
    {
        Id = row.Id,
        WorkspacePath = row.WorkspacePath,
        Title = row.Title,
        Goal = row.Goal,
        Status = row.Status,
        BackendProfileId = row.BackendProfileId,
        Model = row.Model,
        CreatedAt = ParseDate(row.CreatedAt),
        UpdatedAt = ParseDate(row.UpdatedAt),
        CompletedAt = ParseNullableDate(row.CompletedAt)
    };

    private static CodingRunEventRecord ToEventRecord(CodingEventRow row) => new()
    {
        Id = row.Id,
        CodingRunId = row.CodingRunId,
        Sequence = row.Sequence,
        Kind = row.Kind,
        Title = row.Title,
        Detail = row.Detail,
        Status = row.Status,
        RawJson = row.RawJson,
        CreatedAt = ParseDate(row.CreatedAt)
    };

    private static CodingFileChangeProposalRecord ToFileChangeRecord(CodingFileChangeRow row) => new()
    {
        Id = row.Id,
        CodingRunId = row.CodingRunId,
        EventId = row.EventId,
        RelativePath = row.RelativePath,
        ChangeType = row.ChangeType,
        OriginalSha256 = row.OriginalSha256,
        ProposedContent = row.ProposedContent,
        DiffText = row.DiffText,
        Summary = row.Summary,
        Status = row.Status,
        CreatedAt = ParseDate(row.CreatedAt),
        AppliedAt = ParseNullableDate(row.AppliedAt)
    };

    private static CodingCommandProposalRecord ToCommandRecord(CodingCommandRow row) => new()
    {
        Id = row.Id,
        CodingRunId = row.CodingRunId,
        EventId = row.EventId,
        Command = row.Command,
        WorkingDirectory = row.WorkingDirectory,
        Reason = row.Reason,
        RiskLevel = row.RiskLevel,
        Status = row.Status,
        Stdout = row.Stdout,
        Stderr = row.Stderr,
        ExitCode = row.ExitCode,
        CreatedAt = ParseDate(row.CreatedAt),
        CompletedAt = ParseNullableDate(row.CompletedAt)
    };

    private static DateTimeOffset ParseDate(string value) => DateTimeOffset.Parse(value);

    private static DateTimeOffset? ParseNullableDate(string? value) => DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;

    private sealed class CodingRunRow
    {
        public string Id { get; init; } = "";
        public string WorkspacePath { get; init; } = "";
        public string Title { get; init; } = "";
        public string Goal { get; init; } = "";
        public string Status { get; init; } = "";
        public string? BackendProfileId { get; init; }
        public string Model { get; init; } = "";
        public string CreatedAt { get; init; } = "";
        public string UpdatedAt { get; init; } = "";
        public string? CompletedAt { get; init; }
    }

    private sealed class CodingEventRow
    {
        public long Id { get; init; }
        public string CodingRunId { get; init; } = "";
        public int Sequence { get; init; }
        public string Kind { get; init; } = "";
        public string Title { get; init; } = "";
        public string? Detail { get; init; }
        public string Status { get; init; } = "";
        public string? RawJson { get; init; }
        public string CreatedAt { get; init; } = "";
    }

    private sealed class CodingFileChangeRow
    {
        public long Id { get; init; }
        public string CodingRunId { get; init; } = "";
        public long? EventId { get; init; }
        public string RelativePath { get; init; } = "";
        public string ChangeType { get; init; } = "";
        public string? OriginalSha256 { get; init; }
        public string ProposedContent { get; init; } = "";
        public string DiffText { get; init; } = "";
        public string Summary { get; init; } = "";
        public string Status { get; init; } = "";
        public string CreatedAt { get; init; } = "";
        public string? AppliedAt { get; init; }
    }

    private sealed class CodingCommandRow
    {
        public long Id { get; init; }
        public string CodingRunId { get; init; } = "";
        public long? EventId { get; init; }
        public string Command { get; init; } = "";
        public string WorkingDirectory { get; init; } = "";
        public string Reason { get; init; } = "";
        public string RiskLevel { get; init; } = "";
        public string Status { get; init; } = "";
        public string? Stdout { get; init; }
        public string? Stderr { get; init; }
        public int? ExitCode { get; init; }
        public string CreatedAt { get; init; } = "";
        public string? CompletedAt { get; init; }
    }
}
