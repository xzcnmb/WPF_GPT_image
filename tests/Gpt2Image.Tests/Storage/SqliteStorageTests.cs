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
        Assert.Contains("coding_runs", tables);
        Assert.Contains("coding_events", tables);
        Assert.Contains("coding_file_change_proposals", tables);
        Assert.Contains("coding_command_proposals", tables);
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
    public void Initialize_marks_pending_and_running_coding_runs_as_interrupted()
    {
        var paths = CreatePaths();
        var database = new SqliteDatabase(paths);
        var initializer = new SqliteSchemaInitializer(database);
        initializer.Initialize();

        using (var connection = database.OpenConnection())
        {
            connection.Execute(
                @"
                insert into coding_runs (id, workspace_path, title, goal, status, model, created_at, updated_at)
                values ('pending-coding', 'C:\\workspace', 'pending', 'goal', 'pending', 'gpt-4o-mini', '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z'),
                       ('running-coding', 'C:\\workspace', 'running', 'goal', 'running', 'gpt-4o-mini', '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z'),
                       ('waiting-coding', 'C:\\workspace', 'waiting', 'goal', 'waiting_for_approval', 'gpt-4o-mini', '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z')
                ");
        }

        initializer.Initialize();

        using var verify = database.OpenConnection();
        Assert.Equal("interrupted", verify.ExecuteScalar<string>("select status from coding_runs where id = 'pending-coding'"));
        Assert.Equal("interrupted", verify.ExecuteScalar<string>("select status from coding_runs where id = 'running-coding'"));
        Assert.Equal("waiting_for_approval", verify.ExecuteScalar<string>("select status from coding_runs where id = 'waiting-coding'"));
    }

    [Fact]
    public void Initialize_marks_existing_openai_responses_profiles_as_agent_capable()
    {
        var paths = CreatePaths();
        var database = new SqliteDatabase(paths);
        using (var connection = database.OpenConnection())
        {
            connection.Execute(
                @"
                create table backend_profiles (
                    id text primary key,
                    name text not null,
                    base_url text not null,
                    protocol text not null default 'openai-images',
                    api_key_ciphertext text not null,
                    mainline_model text not null,
                    image_model text not null,
                    video_model text not null default '',
                    concurrency integer not null default 1,
                    priority integer not null default 0,
                    is_enabled integer not null default 1,
                    failure_cooldown_until text null,
                    created_at text not null,
                    updated_at text not null
                );
                insert into backend_profiles (
                    id, name, base_url, protocol, api_key_ciphertext, mainline_model, image_model, video_model,
                    concurrency, priority, is_enabled, created_at, updated_at
                ) values (
                    'responses-profile', 'Responses', 'https://example.test/v1', 'openai-responses', 'sk-test', 'gpt-4o-mini', 'gpt-image-2', '',
                    1, 0, 1, '2026-06-06T00:00:00Z', '2026-06-06T00:00:00Z'
                );
                ");
        }

        new SqliteSchemaInitializer(database).Initialize();

        using var verify = database.OpenConnection();
        Assert.Equal(1, verify.ExecuteScalar<int>("select supports_agent from backend_profiles where id = 'responses-profile'"));
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
            ProviderKind = BackendProviderKind.DeepSeek,
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
        Assert.Equal(BackendProviderKind.DeepSeek, loaded.ProviderKind);
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
