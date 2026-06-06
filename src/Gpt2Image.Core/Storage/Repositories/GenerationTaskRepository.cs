using Dapper;

namespace Gpt2Image.Core.Storage.Repositories;

public sealed class GenerationTaskRecord
{
    public string Id { get; init; } = "";
    public string Mode { get; init; } = "";
    public string Prompt { get; init; } = "";
    public string ParametersJson { get; init; } = "";
    public string Status { get; init; } = "";
    public string? BackendProfileId { get; init; }
    public string? Error { get; init; }
    public int RetryCount { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed class GenerationOutputRecord
{
    public int OutputIndex { get; init; }
    public string OutputRole { get; init; } = "";
    public string FilePath { get; init; } = "";
    public string MimeType { get; init; } = "";
    public int? Width { get; init; }
    public int? Height { get; init; }
    public string Sha256 { get; init; } = "";
    public string? RevisedPrompt { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

public sealed class GenerationTaskRepository
{
    private readonly SqliteDatabase _database;

    public GenerationTaskRepository(SqliteDatabase database)
    {
        _database = database;
    }

    public void CreateTask(GenerationTaskRecord task)
    {
        using var connection = _database.OpenConnection();
        connection.Execute(
            @"
            insert into generation_tasks (
                id, mode, prompt, parameters_json, status, backend_profile_id, error,
                retry_count, created_at, started_at, completed_at, updated_at
            )
            values (
                @Id, @Mode, @Prompt, @ParametersJson, @Status, @BackendProfileId, @Error,
                @RetryCount, @CreatedAt, @StartedAt, @CompletedAt, @UpdatedAt
            )
            ",
            new
            {
                task.Id,
                task.Mode,
                task.Prompt,
                task.ParametersJson,
                task.Status,
                task.BackendProfileId,
                task.Error,
                task.RetryCount,
                CreatedAt = task.CreatedAt.ToString("O"),
                StartedAt = task.StartedAt?.ToString("O"),
                CompletedAt = task.CompletedAt?.ToString("O"),
                UpdatedAt = task.UpdatedAt.ToString("O")
            });
    }

    public void MarkRunning(string taskId)
    {
        using var connection = _database.OpenConnection();
        var now = DateTimeOffset.UtcNow.ToString("O");
        connection.Execute(
            @"
            update generation_tasks
            set status = 'running',
                started_at = coalesce(started_at, @Now),
                updated_at = @Now
            where id = @TaskId
            ",
            new { TaskId = taskId, Now = now });
    }

    public void AddOutput(string taskId, GenerationOutputRecord output)
    {
        using var connection = _database.OpenConnection();
        connection.Execute(
            @"
            insert into generation_outputs (
                task_id, output_index, output_role, file_path, mime_type,
                width, height, sha256, revised_prompt, created_at
            )
            values (
                @TaskId, @OutputIndex, @OutputRole, @FilePath, @MimeType,
                @Width, @Height, @Sha256, @RevisedPrompt, @CreatedAt
            )
            ",
            new
            {
                TaskId = taskId,
                output.OutputIndex,
                output.OutputRole,
                output.FilePath,
                output.MimeType,
                output.Width,
                output.Height,
                output.Sha256,
                output.RevisedPrompt,
                CreatedAt = output.CreatedAt.ToString("O")
            });
    }

    public void MarkCompleted(string taskId)
    {
        using var connection = _database.OpenConnection();
        var now = DateTimeOffset.UtcNow.ToString("O");
        connection.Execute(
            @"
            update generation_tasks
            set status = 'completed',
                completed_at = @Now,
                updated_at = @Now,
                error = null
            where id = @TaskId
            ",
            new { TaskId = taskId, Now = now });
    }

    public void MarkFailed(string taskId, string error)
    {
        using var connection = _database.OpenConnection();
        var now = DateTimeOffset.UtcNow.ToString("O");
        connection.Execute(
            @"
            update generation_tasks
            set status = 'failed',
                completed_at = @Now,
                updated_at = @Now,
                error = @Error
            where id = @TaskId
            ",
            new { TaskId = taskId, Error = error, Now = now });
    }
}
