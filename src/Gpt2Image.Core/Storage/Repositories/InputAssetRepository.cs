using Dapper;

namespace Gpt2Image.Core.Storage.Repositories;

public sealed class InputAssetRecord
{
    public long Id { get; init; }
    public string? TaskId { get; init; }
    public string FilePath { get; init; } = "";
    public string MimeType { get; init; } = "";
    public string Sha256 { get; init; } = "";
    public string? SourceTaskId { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

public sealed class InputAssetRepository
{
    private readonly SqliteDatabase _database;

    public InputAssetRepository(SqliteDatabase database)
    {
        _database = database;
    }

    public long Add(InputAssetRecord asset)
    {
        using var connection = _database.OpenConnection();
        connection.Execute(
            @"
            insert into input_assets (
                task_id, file_path, mime_type, sha256, source_task_id, created_at
            )
            values (
                @TaskId, @FilePath, @MimeType, @Sha256, @SourceTaskId, @CreatedAt
            )
            ",
            new
            {
                asset.TaskId,
                asset.FilePath,
                asset.MimeType,
                asset.Sha256,
                asset.SourceTaskId,
                CreatedAt = asset.CreatedAt.ToString("O")
            });
        return connection.ExecuteScalar<long>("select last_insert_rowid()");
    }

    public IReadOnlyList<InputAssetRecord> ListByTaskId(string taskId)
    {
        using var connection = _database.OpenConnection();
        return connection.Query<InputAssetRow>(
                @"
                select
                    id as Id,
                    task_id as TaskId,
                    file_path as FilePath,
                    mime_type as MimeType,
                    sha256 as Sha256,
                    source_task_id as SourceTaskId,
                    created_at as CreatedAt
                from input_assets
                where task_id = @TaskId
                order by id
                ",
                new { TaskId = taskId })
            .Select(row => row.ToRecord())
            .ToList();
    }

    private sealed class InputAssetRow
    {
        public long Id { get; init; }
        public string? TaskId { get; init; }
        public string FilePath { get; init; } = "";
        public string MimeType { get; init; } = "";
        public string Sha256 { get; init; } = "";
        public string? SourceTaskId { get; init; }
        public string CreatedAt { get; init; } = "";

        public InputAssetRecord ToRecord() => new()
        {
            Id = Id,
            TaskId = TaskId,
            FilePath = FilePath,
            MimeType = MimeType,
            Sha256 = Sha256,
            SourceTaskId = SourceTaskId,
            CreatedAt = DateTimeOffset.Parse(CreatedAt)
        };
    }
}
