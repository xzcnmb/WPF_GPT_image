using Dapper;
using Gpt2Image.Core.Models;
using Gpt2Image.Core.Storage;
using Gpt2Image.Core.Storage.Repositories;
using Microsoft.Data.Sqlite;

namespace Gpt2Image.Tests.Storage;

public sealed class GenerationTaskRepositoryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "gpt2image-task-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void CompleteTask_persists_status_and_output_metadata()
    {
        var database = new SqliteDatabase(AppPaths.CreateForRoot(_root));
        new SqliteSchemaInitializer(database).Initialize();
        var repository = new GenerationTaskRepository(database);

        repository.CreateTask(new GenerationTaskRecord
        {
            Id = "task-1",
            Mode = "generate",
            Prompt = "prompt",
            ParametersJson = "{\"size\":\"1024x1024\"}",
            Status = "pending",
            CreatedAt = DateTimeOffset.Parse("2026-06-06T00:00:00Z"),
            UpdatedAt = DateTimeOffset.Parse("2026-06-06T00:00:00Z")
        });
        repository.MarkRunning("task-1");
        repository.AddOutput("task-1", new GenerationOutputRecord
        {
            OutputIndex = 0,
            OutputRole = "final",
            FilePath = @"C:\images\task-1_0.png",
            MimeType = "image/png",
            Sha256 = "abc",
            RevisedPrompt = "revised",
            CreatedAt = DateTimeOffset.Parse("2026-06-06T00:01:00Z")
        });
        repository.MarkCompleted("task-1");

        using var connection = database.OpenConnection();
        Assert.Equal("completed", connection.ExecuteScalar<string>("select status from generation_tasks where id = 'task-1'"));
        Assert.Equal("revised", connection.ExecuteScalar<string>("select revised_prompt from generation_outputs where task_id = 'task-1'"));
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
