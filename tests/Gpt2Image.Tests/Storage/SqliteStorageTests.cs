using Dapper;
using Gpt2Image.Core.Models;
using Gpt2Image.Core.Security;
using Gpt2Image.Core.Storage;
using Gpt2Image.Core.Storage.Repositories;
using Microsoft.Data.Sqlite;

namespace Gpt2Image.Tests.Storage;

public sealed class SqliteStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "gpt2image-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Initialize_creates_schema_and_enables_wal()
    {
        var paths = CreatePaths();
        var database = new SqliteDatabase(paths);
        var initializer = new SqliteSchemaInitializer(database);

        initializer.Initialize();

        using var connection = database.OpenConnection();
        var tables = connection.Query<string>("select name from sqlite_master where type = 'table'").ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("backend_profiles", tables);
        Assert.Contains("generation_tasks", tables);
        Assert.Contains("generation_outputs", tables);
        Assert.Contains("input_assets", tables);
        Assert.Contains("agent_events", tables);
        Assert.Contains("chat_conversations", tables);
        Assert.Contains("chat_messages", tables);
        Assert.Contains("schema_migrations", tables);
        Assert.Equal("wal", connection.ExecuteScalar<string>("PRAGMA journal_mode"));
    }

    [Fact]
    public void Initialize_marks_pending_and_running_tasks_as_interrupted()
    {
        var paths = CreatePaths();
        var database = new SqliteDatabase(paths);
        var initializer = new SqliteSchemaInitializer(database);
        initializer.Initialize();

        using (var connection = database.OpenConnection())
        {
            connection.Execute(
                @"
                insert into generation_tasks (id, mode, prompt, parameters_json, status, created_at, updated_at)
                values ('pending-id', 'generate', 'p', '{}', 'pending', '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z'),
                       ('running-id', 'generate', 'p', '{}', 'running', '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z'),
                       ('done-id', 'generate', 'p', '{}', 'completed', '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z')
                ");
        }

        initializer.Initialize();

        using var verify = database.OpenConnection();
        Assert.Equal("interrupted", verify.ExecuteScalar<string>("select status from generation_tasks where id = 'pending-id'"));
        Assert.Equal("interrupted", verify.ExecuteScalar<string>("select status from generation_tasks where id = 'running-id'"));
        Assert.Equal("completed", verify.ExecuteScalar<string>("select status from generation_tasks where id = 'done-id'"));
    }

    [Fact]
    public void Backend_profile_repository_encrypts_api_key_at_rest_and_roundtrips_plaintext()
    {
        var paths = CreatePaths();
        var database = new SqliteDatabase(paths);
        new SqliteSchemaInitializer(database).Initialize();
        var repository = new BackendProfileRepository(database, new DpapiSecretProtector());

        repository.Upsert(new BackendProfile
        {
            Id = "profile-1",
            Name = "OpenAI",
            BaseUrl = "https://api.openai.com/v1",
            ApiKey = "sk-secret-value",
            Protocol = BackendProtocol.OpenAiResponses,
            MainlineModel = "gpt-5.5",
            ImageModel = "gpt-image-2",
            Concurrency = 2,
            Priority = 10,
            IsEnabled = true
        });

        using var connection = database.OpenConnection();
        var stored = connection.ExecuteScalar<string>("select api_key_ciphertext from backend_profiles where id = 'profile-1'");
        Assert.NotNull(stored);
        Assert.DoesNotContain("sk-secret-value", stored, StringComparison.Ordinal);

        var loaded = repository.GetById("profile-1");
        Assert.NotNull(loaded);
        Assert.Equal("sk-secret-value", loaded.ApiKey);
        Assert.Equal(BackendProtocol.OpenAiResponses, loaded.Protocol);
        Assert.Equal("gpt-image-2", loaded.ImageModel);
    }

    private AppPaths CreatePaths() => AppPaths.CreateForRoot(_root);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
