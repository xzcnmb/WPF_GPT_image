using Dapper;

namespace Gpt2Image.Core.Storage;

public sealed class SqliteSchemaInitializer
{
    private readonly SqliteDatabase _database;

    public SqliteSchemaInitializer(SqliteDatabase database)
    {
        _database = database;
    }

    public void Initialize()
    {
        using var connection = _database.OpenConnection();
        connection.Execute("PRAGMA journal_mode = WAL;");
        using var transaction = connection.BeginTransaction();
        connection.Execute(SchemaSql, transaction: transaction);
        EnsureBackendProfileProtocolColumn(connection, transaction);
        EnsureGenerationOutputColumns(connection, transaction);
        connection.Execute(
            @"
            insert or ignore into schema_migrations (version, applied_at)
            values (1, @AppliedAt)
            ",
            new { AppliedAt = DateTimeOffset.UtcNow.ToString("O") },
            transaction);
        connection.Execute(
            @"
            insert or ignore into schema_migrations (version, applied_at)
            values (2, @AppliedAt)
            ",
            new { AppliedAt = DateTimeOffset.UtcNow.ToString("O") },
            transaction);
        connection.Execute(
            @"
            insert or ignore into schema_migrations (version, applied_at)
            values (3, @AppliedAt)
            ",
            new { AppliedAt = DateTimeOffset.UtcNow.ToString("O") },
            transaction);
        connection.Execute(
            @"
            update generation_tasks
            set status = 'interrupted',
                error = coalesce(error, 'Application exited before this task completed.'),
                updated_at = @UpdatedAt
            where status in ('pending', 'running')
            ",
            new { UpdatedAt = DateTimeOffset.UtcNow.ToString("O") },
            transaction);
        transaction.Commit();
    }

    private static void EnsureBackendProfileProtocolColumn(
        System.Data.IDbConnection connection,
        System.Data.IDbTransaction transaction)
    {
        var columns = connection.Query<string>(
                "select name from pragma_table_info('backend_profiles')",
                transaction: transaction)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (columns.Contains("protocol"))
        {
            return;
        }

        connection.Execute(
            "alter table backend_profiles add column protocol text not null default 'openai-images';",
            transaction: transaction);
    }

    private static void EnsureGenerationOutputColumns(
        System.Data.IDbConnection connection,
        System.Data.IDbTransaction transaction)
    {
        var columns = connection.Query<string>(
                "select name from pragma_table_info('generation_outputs')",
                transaction: transaction)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!columns.Contains("image_base64"))
        {
            connection.Execute(
                "alter table generation_outputs add column image_base64 text null;",
                transaction: transaction);
        }

        if (!columns.Contains("source_url"))
        {
            connection.Execute(
                "alter table generation_outputs add column source_url text null;",
                transaction: transaction);
        }
    }

    private const string SchemaSql =
        @"
        create table if not exists schema_migrations (
            version integer primary key,
            applied_at text not null
        );

        create table if not exists backend_profiles (
            id text primary key,
            name text not null,
            base_url text not null,
            protocol text not null default 'openai-images',
            api_key_ciphertext text not null,
            mainline_model text not null,
            image_model text not null,
            concurrency integer not null default 1,
            priority integer not null default 0,
            is_enabled integer not null default 1,
            failure_cooldown_until text null,
            created_at text not null,
            updated_at text not null
        );

        create table if not exists generation_tasks (
            id text primary key,
            mode text not null,
            prompt text not null,
            parameters_json text not null,
            status text not null,
            backend_profile_id text null references backend_profiles(id) on delete set null,
            error text null,
            retry_count integer not null default 0,
            created_at text not null,
            started_at text null,
            completed_at text null,
            updated_at text not null,
            deleted_at text null
        );

        create table if not exists generation_outputs (
            id integer primary key autoincrement,
            task_id text not null references generation_tasks(id) on delete cascade,
            output_index integer not null,
            output_role text not null,
            file_path text not null,
            mime_type text not null,
            width integer null,
            height integer null,
            sha256 text not null,
            revised_prompt text null,
            image_base64 text null,
            source_url text null,
            created_at text not null
        );

        create table if not exists input_assets (
            id integer primary key autoincrement,
            task_id text null references generation_tasks(id) on delete set null,
            file_path text not null,
            mime_type text not null,
            sha256 text not null,
            source_task_id text null references generation_tasks(id) on delete set null,
            created_at text not null
        );

        create table if not exists agent_runs (
            id text primary key,
            goal text not null,
            status text not null,
            max_rounds integer not null,
            previous_response_id text null,
            final_task_id text null references generation_tasks(id) on delete set null,
            created_at text not null,
            updated_at text not null
        );

        create table if not exists agent_events (
            id text primary key,
            agent_run_id text not null references agent_runs(id) on delete cascade,
            round integer not null,
            kind text not null,
            title text not null,
            detail text null,
            status text not null,
            raw_json text null,
            image_output_id integer null references generation_outputs(id) on delete set null,
            image_base64 text null,
            created_at text not null
        );

        create table if not exists chat_conversations (
            id text primary key,
            title text not null,
            backend_profile_id text null references backend_profiles(id) on delete set null,
            model text not null,
            created_at text not null,
            updated_at text not null,
            deleted_at text null
        );

        create table if not exists chat_messages (
            id integer primary key autoincrement,
            conversation_id text not null references chat_conversations(id) on delete cascade,
            role text not null,
            content text not null,
            raw_json text null,
            created_at text not null
        );

        create table if not exists app_settings (
            key text primary key,
            value text not null,
            updated_at text not null
        );

        create index if not exists ix_generation_tasks_created_at on generation_tasks(created_at desc);
        create index if not exists ix_generation_outputs_task_id on generation_outputs(task_id);
        create index if not exists ix_agent_events_agent_run_round on agent_events(agent_run_id, round);
        create index if not exists ix_chat_conversations_updated_at on chat_conversations(updated_at desc);
        create index if not exists ix_chat_messages_conversation_id on chat_messages(conversation_id, id);
        ";
}
