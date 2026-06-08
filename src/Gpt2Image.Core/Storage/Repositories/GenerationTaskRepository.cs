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
    public string? ImageBase64 { get; init; }
    public string? SourceUrl { get; init; }
    public string MediaType { get; init; } = "image";
    public double? DurationSeconds { get; init; }
    public string? ProviderRequestId { get; init; }
    public string? MetadataJson { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

public sealed class GenerationTaskHistoryRecord
{
    public string Id { get; init; } = "";
    public string Mode { get; init; } = "";
    public string Prompt { get; init; } = "";
    public string ParametersJson { get; init; } = "";
    public string Status { get; init; } = "";
    public string? Error { get; init; }
    public string CreatedAt { get; init; } = "";
    public string? CompletedAt { get; init; }
    public string UpdatedAt { get; init; } = "";
    public int OutputCount { get; init; }
    public string? PreviewBase64 { get; init; }
    public string? PreviewFilePath { get; init; }
    public string? PreviewMediaType { get; init; }
}


public sealed class GenerationTaskOutputRecord
{
    public int OutputIndex { get; init; }
    public string OutputRole { get; init; } = "";
    public string FilePath { get; init; } = "";
    public string MimeType { get; init; } = "";
    public string Sha256 { get; init; } = "";
    public string? RevisedPrompt { get; init; }
    public string? ImageBase64 { get; init; }
    public string? SourceUrl { get; init; }
    public string MediaType { get; init; } = "image";
    public double? DurationSeconds { get; init; }
    public string? ProviderRequestId { get; init; }
    public string? MetadataJson { get; init; }
    public string CreatedAt { get; init; } = "";
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
                width, height, sha256, revised_prompt, image_base64, source_url,
                media_type, duration_seconds, provider_request_id, metadata_json, created_at
            )
            values (
                @TaskId, @OutputIndex, @OutputRole, @FilePath, @MimeType,
                @Width, @Height, @Sha256, @RevisedPrompt, @ImageBase64, @SourceUrl,
                @MediaType, @DurationSeconds, @ProviderRequestId, @MetadataJson, @CreatedAt
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
                output.ImageBase64,
                output.SourceUrl,
                output.MediaType,
                output.DurationSeconds,
                output.ProviderRequestId,
                output.MetadataJson,
                CreatedAt = output.CreatedAt.ToString("O")
            });
    }

    public IReadOnlyList<GenerationTaskHistoryRecord> ListRecent(int limit = 100)
    {
        using var connection = _database.OpenConnection();
        return connection.Query<GenerationTaskHistoryRecord>(
            @"
            select
                t.id as Id,
                t.mode as Mode,
                t.prompt as Prompt,
                t.parameters_json as ParametersJson,
                t.status as Status,
                t.error as Error,
                t.created_at as CreatedAt,
                t.completed_at as CompletedAt,
                t.updated_at as UpdatedAt,
                count(o.id) as OutputCount,
                (
                    select o2.image_base64
                    from generation_outputs o2
                    where o2.task_id = t.id and o2.image_base64 is not null and o2.image_base64 <> ''
                    order by o2.output_index, o2.id
                    limit 1
                ) as PreviewBase64,
                (
                    select o3.file_path
                    from generation_outputs o3
                    where o3.task_id = t.id and o3.file_path is not null and o3.file_path <> ''
                    order by o3.output_index, o3.id
                    limit 1
                ) as PreviewFilePath,
                (
                    select o4.media_type
                    from generation_outputs o4
                    where o4.task_id = t.id
                    order by o4.output_index, o4.id
                    limit 1
                ) as PreviewMediaType
            from generation_tasks t
            left join generation_outputs o on o.task_id = t.id
            where t.deleted_at is null
            group by t.id
            order by t.created_at desc
            limit @Limit
            ",
            new { Limit = Math.Max(1, limit) })
            .ToList();
    }

    public IReadOnlyList<GenerationTaskOutputRecord> ListOutputs(string taskId)
    {
        using var connection = _database.OpenConnection();
        return connection.Query<GenerationTaskOutputRecord>(
            @"
            select
                output_index as OutputIndex,
                output_role as OutputRole,
                file_path as FilePath,
                mime_type as MimeType,
                sha256 as Sha256,
                revised_prompt as RevisedPrompt,
                image_base64 as ImageBase64,
                source_url as SourceUrl,
                media_type as MediaType,
                duration_seconds as DurationSeconds,
                provider_request_id as ProviderRequestId,
                metadata_json as MetadataJson,
                created_at as CreatedAt
            from generation_outputs
            where task_id = @TaskId
            order by output_index, id
            ",
            new { TaskId = taskId })
            .ToList();
    }

    public void UpdateOutputFilePath(string taskId, int outputIndex, string filePath)
    {
        using var connection = _database.OpenConnection();
        connection.Execute(
            @"
            update generation_outputs
            set file_path = @FilePath
            where task_id = @TaskId and output_index = @OutputIndex
            ",
            new { TaskId = taskId, OutputIndex = outputIndex, FilePath = filePath });
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
