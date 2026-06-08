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
        using var transaction = connection.BeginTransaction();
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
            },
            transaction);
        var messageId = connection.ExecuteScalar<long>("select last_insert_rowid()", transaction: transaction);
        foreach (var attachment in message.Attachments)
        {
            AddAttachment(connection, transaction, messageId, message.ConversationId, attachment, message.CreatedAt);
        }

        transaction.Commit();
        return messageId;
    }

    public IReadOnlyList<ChatMessage> ListMessages(string conversationId)
    {
        using var connection = _database.OpenConnection();
        var messages = connection.Query<ChatMessageRow>(
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
        if (messages.Count == 0)
        {
            return messages;
        }

        var attachments = connection.Query<ChatAttachmentRow>(
                @"
                select
                    id as Id,
                    message_id as MessageId,
                    conversation_id as ConversationId,
                    file_path as FilePath,
                    file_name as FileName,
                    mime_type as MimeType,
                    sha256 as Sha256,
                    byte_length as ByteLength,
                    created_at as CreatedAt
                from chat_message_attachments
                where conversation_id = @ConversationId
                order by id
                ",
                new { ConversationId = conversationId })
            .Select(row => row.ToModel())
            .GroupBy(attachment => attachment.MessageId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<ChatAttachment>)group.ToList());

        return messages.Select(message => new ChatMessage
            {
                Id = message.Id,
                ConversationId = message.ConversationId,
                Role = message.Role,
                Content = message.Content,
                Attachments = attachments.TryGetValue(message.Id, out var list) ? list : Array.Empty<ChatAttachment>(),
                RawJson = message.RawJson,
                CreatedAt = message.CreatedAt
            })
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

    private static void AddAttachment(
        System.Data.IDbConnection connection,
        System.Data.IDbTransaction transaction,
        long messageId,
        string conversationId,
        ChatAttachment attachment,
        DateTimeOffset fallbackCreatedAt)
    {
        connection.Execute(
            @"
            insert into chat_message_attachments (
                message_id, conversation_id, file_path, file_name, mime_type, sha256, byte_length, created_at
            )
            values (
                @MessageId, @ConversationId, @FilePath, @FileName, @MimeType, @Sha256, @ByteLength, @CreatedAt
            )
            ",
            new
            {
                MessageId = messageId,
                ConversationId = conversationId,
                attachment.FilePath,
                FileName = string.IsNullOrWhiteSpace(attachment.FileName) ? Path.GetFileName(attachment.FilePath) : attachment.FileName,
                attachment.MimeType,
                attachment.Sha256,
                attachment.ByteLength,
                CreatedAt = (attachment.CreatedAt == default ? fallbackCreatedAt : attachment.CreatedAt).ToString("O")
            },
            transaction);
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

    private sealed class ChatAttachmentRow
    {
        public long Id { get; init; }
        public long MessageId { get; init; }
        public string ConversationId { get; init; } = "";
        public string FilePath { get; init; } = "";
        public string FileName { get; init; } = "";
        public string MimeType { get; init; } = "";
        public string Sha256 { get; init; } = "";
        public long ByteLength { get; init; }
        public string CreatedAt { get; init; } = "";

        public ChatAttachment ToModel() => new()
        {
            Id = Id,
            MessageId = MessageId,
            ConversationId = ConversationId,
            FilePath = FilePath,
            FileName = FileName,
            MimeType = MimeType,
            Sha256 = Sha256,
            ByteLength = ByteLength,
            CreatedAt = DateTimeOffset.Parse(CreatedAt)
        };
    }
}
