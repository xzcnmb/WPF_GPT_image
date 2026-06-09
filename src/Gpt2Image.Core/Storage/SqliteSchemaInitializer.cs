using Dapper;
using Gpt2Image.Core.Models;

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
        EnsureBackendProfileColumns(connection, transaction);
        EnsureGenerationOutputColumns(connection, transaction);
        EnsureChatAttachmentColumns(connection, transaction);
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
        var interruptedAt = DateTimeOffset.UtcNow.ToString("O");
        connection.Execute(
            @"
            update generation_tasks
            set status = 'interrupted',
                error = coalesce(error, 'Application exited before this task completed.'),
                updated_at = @UpdatedAt
            where status in ('pending', 'running')
            ",
            new { UpdatedAt = interruptedAt },
            transaction);
        connection.Execute(
            @"
            update coding_runs
            set status = @InterruptedStatus,
                updated_at = @UpdatedAt,
                completed_at = coalesce(completed_at, @UpdatedAt)
            where status in (@PendingStatus, @RunningStatus)
            ",
            new
            {
                InterruptedStatus = CodingRunStatus.Interrupted,
                PendingStatus = CodingRunStatus.Pending,
                RunningStatus = CodingRunStatus.Running,
                UpdatedAt = interruptedAt
            },
            transaction);
        transaction.Commit();
    }

    private static void EnsureBackendProfileColumns(
        System.Data.IDbConnection connection,
        System.Data.IDbTransaction transaction)
    {
        var columns = connection.Query<string>(
                "select name from pragma_table_info('backend_profiles')",
                transaction: transaction)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!columns.Contains("protocol"))
        {
            connection.Execute(
                "alter table backend_profiles add column protocol text not null default 'openai-images';",
                transaction: transaction);
        }

        if (!columns.Contains("video_model"))
        {
            connection.Execute(
                "alter table backend_profiles add column video_model text not null default '';",
                transaction: transaction);
        }

        if (!columns.Contains("provider_kind"))
        {
            connection.Execute(
                "alter table backend_profiles add column provider_kind text not null default 'custom';",
                transaction: transaction);
        }

        EnsureIntegerColumn(connection, transaction, columns, "supports_prompt", "1");
        EnsureIntegerColumn(connection, transaction, columns, "supports_chat", "1");
        EnsureIntegerColumn(connection, transaction, columns, "supports_image", "1");
        EnsureIntegerColumn(connection, transaction, columns, "supports_video", "0");
        EnsureIntegerColumn(connection, transaction, columns, "supports_agent", "0");

        connection.Execute(
            @"
            update backend_profiles
            set supports_prompt = 0,
                supports_chat = 0,
                supports_image = 0,
                supports_video = 1,
                supports_agent = 0,
                provider_kind = case when provider_kind = 'custom' then 'routin' else provider_kind end
            where lower(protocol) = 'routin-xai-video'
            ",
            transaction: transaction);
        connection.Execute(
            @"
            update backend_profiles
            set supports_agent = 1
            where lower(protocol) = 'openai-responses'
            ",
            transaction: transaction);
    }

    private static void EnsureIntegerColumn(
        System.Data.IDbConnection connection,
        System.Data.IDbTransaction transaction,
        ISet<string> columns,
        string columnName,
        string defaultValue)
    {
        if (columns.Contains(columnName))
        {
            return;
        }

        connection.Execute(
            $"alter table backend_profiles add column {columnName} integer not null default {defaultValue};",
            transaction: transaction);
    }

    private static void EnsureChatAttachmentColumns(
        System.Data.IDbConnection connection,
        System.Data.IDbTransaction transaction)
    {
        connection.Execute(
            @"
            create table if not exists chat_message_attachments (
                id integer primary key autoincrement,
                message_id integer not null references chat_messages(id) on delete cascade,
                conversation_id text not null references chat_conversations(id) on delete cascade,
                file_path text not null,
                file_name text not null,
                mime_type text not null,
                sha256 text not null,
                byte_length integer not null default 0,
                created_at text not null
            );
            create index if not exists ix_chat_message_attachments_message_id on chat_message_attachments(message_id);
            create index if not exists ix_chat_message_attachments_conversation_id on chat_message_attachments(conversation_id);
            ",
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

        if (!columns.Contains("media_type"))
        {
            connection.Execute(
                "alter table generation_outputs add column media_type text not null default 'image';",
                transaction: transaction);
        }

        if (!columns.Contains("duration_seconds"))
        {
            connection.Execute(
                "alter table generation_outputs add column duration_seconds real null;",
                transaction: transaction);
        }

        if (!columns.Contains("provider_request_id"))
        {
            connection.Execute(
                "alter table generation_outputs add column provider_request_id text null;",
                transaction: transaction);
        }

        if (!columns.Contains("metadata_json"))
        {
            connection.Execute(
                "alter table generation_outputs add column metadata_json text null;",
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
            provider_kind text not null default 'custom',
            api_key_ciphertext text not null,
            mainline_model text not null,
            image_model text not null,
            video_model text not null default '',
            concurrency integer not null default 1,
            priority integer not null default 0,
            is_enabled integer not null default 1,
            supports_prompt integer not null default 1,
            supports_chat integer not null default 1,
            supports_image integer not null default 1,
            supports_video integer not null default 0,
            supports_agent integer not null default 0,
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
            media_type text not null default 'image',
            duration_seconds real null,
            provider_request_id text null,
            metadata_json text null,
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

        create table if not exists coding_runs (
            id text primary key,
            workspace_path text not null,
            title text not null,
            goal text not null,
            status text not null,
            backend_profile_id text null references backend_profiles(id) on delete set null,
            model text not null,
            created_at text not null,
            updated_at text not null,
            completed_at text null
        );

        create table if not exists coding_events (
            id integer primary key autoincrement,
            coding_run_id text not null references coding_runs(id) on delete cascade,
            sequence integer not null,
            kind text not null,
            title text not null,
            detail text null,
            status text not null,
            raw_json text null,
            created_at text not null
        );

        create table if not exists coding_file_change_proposals (
            id integer primary key autoincrement,
            coding_run_id text not null references coding_runs(id) on delete cascade,
            event_id integer null references coding_events(id) on delete set null,
            relative_path text not null,
            change_type text not null,
            original_sha256 text null,
            proposed_content text not null,
            diff_text text not null,
            summary text not null,
            status text not null,
            created_at text not null,
            applied_at text null
        );

        create table if not exists coding_command_proposals (
            id integer primary key autoincrement,
            coding_run_id text not null references coding_runs(id) on delete cascade,
            event_id integer null references coding_events(id) on delete set null,
            command text not null,
            working_directory text not null,
            reason text not null,
            risk_level text not null,
            status text not null,
            stdout text null,
            stderr text null,
            exit_code integer null,
            created_at text not null,
            completed_at text null
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

        create table if not exists chat_message_attachments (
            id integer primary key autoincrement,
            message_id integer not null references chat_messages(id) on delete cascade,
            conversation_id text not null references chat_conversations(id) on delete cascade,
            file_path text not null,
            file_name text not null,
            mime_type text not null,
            sha256 text not null,
            byte_length integer not null default 0,
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
        create index if not exists ix_coding_runs_updated_at on coding_runs(updated_at desc);
        create index if not exists ix_coding_events_run_sequence on coding_events(coding_run_id, sequence);
        create index if not exists ix_coding_file_changes_run on coding_file_change_proposals(coding_run_id, id);
        create index if not exists ix_coding_commands_run on coding_command_proposals(coding_run_id, id);
        create index if not exists ix_chat_conversations_updated_at on chat_conversations(updated_at desc);
        create index if not exists ix_chat_messages_conversation_id on chat_messages(conversation_id, id);
        create index if not exists ix_chat_message_attachments_message_id on chat_message_attachments(message_id);
        create index if not exists ix_chat_message_attachments_conversation_id on chat_message_attachments(conversation_id);
        ";
}
