using Dapper;
using Gpt2Image.Core.Models;

namespace Gpt2Image.Core.Storage.Repositories;

public sealed class ChatRepository
{
    private readonly SqliteDatabase _database;

    public ChatRepository(SqliteDatabase database)
    {
        _database = database;
    }

    public IReadOnlyList<ChatConversation> ListConversations(int limit = 100)
    {
        using var connection = _database.OpenConnection();
        return connection.Query<ChatConversationRow>(
                @"
                select
                    id as Id,
                    title as Title,
                    backend_profile_id as BackendProfileId,
                    model as Model,
                    created_at as CreatedAt,
                    updated_at as UpdatedAt
                from chat_conversations
                where deleted_at is null
                order by updated_at desc
                limit @Limit
                ",
                new { Limit = Math.Max(1, limit) })
            .Select(row => row.ToModel())
            .ToList();
    }

    public ChatConversation? GetConversation(string id)
    {
        using var connection = _database.OpenConnection();
        return connection.QuerySingleOrDefault<ChatConversationRow>(
                @"
                select
                    id as Id,
                    title as Title,
                    backend_profile_id as BackendProfileId,
                    model as Model,
                    created_at as CreatedAt,
                    updated_at as UpdatedAt
                from chat_conversations
                where id = @Id and deleted_at is null
                ",
                new { Id = id })
            ?.ToModel();
    }

    public void CreateConversation(ChatConversation conversation)
    {
        using var connection = _database.OpenConnection();
        connection.Execute(
            @"
            insert into chat_conversations (
                id, title, backend_profile_id, model, created_at, updated_at
            )
            values (
                @Id, @Title, @BackendProfileId, @Model, @CreatedAt, @UpdatedAt
            )
            ",
            new
            {
                conversation.Id,
                conversation.Title,
                conversation.BackendProfileId,
                conversation.Model,
                CreatedAt = conversation.CreatedAt.ToString("O"),
                UpdatedAt = conversation.UpdatedAt.ToString("O")
            });
    }

    public void UpdateConversationTitle(string id, string title)
    {
        using var connection = _database.OpenConnection();
        var now = DateTimeOffset.UtcNow.ToString("O");
        connection.Execute(
            @"
            update chat_conversations
            set title = @Title,
                updated_at = @UpdatedAt
            where id = @Id and deleted_at is null
            ",
            new { Id = id, Title = title, UpdatedAt = now });
    }

    public void TouchConversation(string id, DateTimeOffset updatedAt)
    {
        using var connection = _database.OpenConnection();
        connection.Execute(
            @"
            update chat_conversations
            set updated_at = @UpdatedAt
            where id = @Id and deleted_at is null
            ",
            new { Id = id, UpdatedAt = updatedAt.ToString("O") });
    }

    public long AddMessage(ChatMessage message)
    {
        using var connection = _database.OpenConnection();
        connection.Execute(
            @"
            insert into chat_messages (
                conversation_id, role, content, raw_json, created_at
            )
            values (
                @ConversationId, @Role, @Content, @RawJson, @CreatedAt
            )
            ",
            new
            {
                message.ConversationId,
                message.Role,
                message.Content,
                message.RawJson,
                CreatedAt = message.CreatedAt.ToString("O")
            });
        return connection.ExecuteScalar<long>("select last_insert_rowid()");
    }

    public IReadOnlyList<ChatMessage> ListMessages(string conversationId)
    {
        using var connection = _database.OpenConnection();
        return connection.Query<ChatMessageRow>(
                @"
                select
                    id as Id,
                    conversation_id as ConversationId,
                    role as Role,
                    content as Content,
                    raw_json as RawJson,
                    created_at as CreatedAt
                from chat_messages
                where conversation_id = @ConversationId
                order by id
                ",
                new { ConversationId = conversationId })
            .Select(row => row.ToModel())
            .ToList();
    }

    public void SoftDeleteConversation(string id)
    {
        using var connection = _database.OpenConnection();
        var now = DateTimeOffset.UtcNow.ToString("O");
        connection.Execute(
            @"
            update chat_conversations
            set deleted_at = @DeletedAt,
                updated_at = @DeletedAt
            where id = @Id and deleted_at is null
            ",
            new { Id = id, DeletedAt = now });
    }

    private sealed class ChatConversationRow
    {
        public string Id { get; init; } = "";
        public string Title { get; init; } = "";
        public string? BackendProfileId { get; init; }
        public string Model { get; init; } = "";
        public string CreatedAt { get; init; } = "";
        public string UpdatedAt { get; init; } = "";

        public ChatConversation ToModel() => new()
        {
            Id = Id,
            Title = Title,
            BackendProfileId = BackendProfileId,
            Model = Model,
            CreatedAt = DateTimeOffset.Parse(CreatedAt),
            UpdatedAt = DateTimeOffset.Parse(UpdatedAt)
        };
    }

    private sealed class ChatMessageRow
    {
        public long Id { get; init; }
        public string ConversationId { get; init; } = "";
        public string Role { get; init; } = "";
        public string Content { get; init; } = "";
        public string? RawJson { get; init; }
        public string CreatedAt { get; init; } = "";

        public ChatMessage ToModel() => new()
        {
            Id = Id,
            ConversationId = ConversationId,
            Role = Role,
            Content = Content,
            RawJson = RawJson,
            CreatedAt = DateTimeOffset.Parse(CreatedAt)
        };
    }
}
