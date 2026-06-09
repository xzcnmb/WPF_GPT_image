using Gpt2Image.Core.Storage;
using Gpt2Image.Core.Storage.Repositories;
using Microsoft.Data.Sqlite;

namespace Gpt2Image.Tests.Storage;

public sealed class InputAssetRepositoryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "gpt2image-input-asset-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Add_and_list_task_assets_roundtrip_successfully()
    {
        var database = new SqliteDatabase(AppPaths.CreateForRoot(_root));
        new SqliteSchemaInitializer(database).Initialize();
        var taskRepository = new GenerationTaskRepository(database);
        var repository = new InputAssetRepository(database);
        var createdAt = DateTimeOffset.Parse("2026-06-06T12:00:00Z");

        taskRepository.CreateTask(new GenerationTaskRecord
        {
            Id = "task-1",
            Mode = "edit",
            Prompt = "edit image",
            ParametersJson = "{}",
            Status = "pending",
            CreatedAt = createdAt,
            UpdatedAt = createdAt
        });

        repository.Add(new InputAssetRecord
        {
            TaskId = "task-1",
            FilePath = @"C:\images\input.png",
            MimeType = "image/png",
            Sha256 = "abc123",
            CreatedAt = createdAt
        });

        var asset = Assert.Single(repository.ListByTaskId("task-1"));
        Assert.Equal("image/png", asset.MimeType);
        Assert.Equal("abc123", asset.Sha256);
        Assert.Equal(@"C:\images\input.png", asset.FilePath);
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
