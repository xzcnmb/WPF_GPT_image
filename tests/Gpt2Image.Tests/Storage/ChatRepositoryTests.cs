using Gpt2Image.Core.Models;
using Gpt2Image.Core.Storage;
using Gpt2Image.Core.Storage.Repositories;
using Microsoft.Data.Sqlite;

namespace Gpt2Image.Tests.Storage;

public sealed class ChatRepositoryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "gpt2image-chat-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void CreateConversation_and_add_messages_roundtrip_successfully()
    {
        var database = new SqliteDatabase(AppPaths.CreateForRoot(_root));
        new SqliteSchemaInitializer(database).Initialize();
        var repository = new ChatRepository(database);
        var createdAt = DateTimeOffset.Parse("2026-06-06T12:00:00Z");

        repository.CreateConversation(new ChatConversation
        {
            Id = "chat-1",
            Title = "测试会话",
            Model = "gpt-4o-mini",
            CreatedAt = createdAt,
            UpdatedAt = createdAt
        });
        repository.AddMessage(new ChatMessage
        {
            ConversationId = "chat-1",
            Role = "user",
            Content = "你好",
            CreatedAt = createdAt
        });
        repository.AddMessage(new ChatMessage
        {
            ConversationId = "chat-1",
            Role = "assistant",
            Content = "你好，我在。",
            RawJson = "{\"ok\":true}",
            CreatedAt = createdAt.AddMinutes(1)
        });

        var conversation = Assert.Single(repository.ListConversations());
        Assert.Equal("测试会话", conversation.Title);
        var messages = repository.ListMessages("chat-1");
        Assert.Equal(2, messages.Count);
        Assert.Equal("user", messages[0].Role);
        Assert.Equal("assistant", messages[1].Role);
        Assert.Equal("{\"ok\":true}", messages[1].RawJson);
    }

    [Fact]
    public void SoftDeleteConversation_hides_deleted_conversation_from_list()
    {
        var database = new SqliteDatabase(AppPaths.CreateForRoot(_root));
        new SqliteSchemaInitializer(database).Initialize();
        var repository = new ChatRepository(database);
        var now = DateTimeOffset.Parse("2026-06-06T12:00:00Z");

        repository.CreateConversation(new ChatConversation
        {
            Id = "chat-1",
            Title = "测试会话",
            Model = "gpt-4o-mini",
            CreatedAt = now,
            UpdatedAt = now
        });

        repository.SoftDeleteConversation("chat-1");

        Assert.Empty(repository.ListConversations());
        Assert.Null(repository.GetConversation("chat-1"));
    }

    [Fact]
    public void AddMessage_with_attachment_roundtrips_attachment_metadata()
    {
        var database = new SqliteDatabase(AppPaths.CreateForRoot(_root));
        new SqliteSchemaInitializer(database).Initialize();
        var repository = new ChatRepository(database);
        var now = DateTimeOffset.Parse("2026-06-06T12:00:00Z");
        repository.CreateConversation(new ChatConversation
        {
            Id = "chat-attachments",
            Title = "附件会话",
            Model = "gpt-4o-mini",
            CreatedAt = now,
            UpdatedAt = now
        });

        var messageId = repository.AddMessage(new ChatMessage
        {
            ConversationId = "chat-attachments",
            Role = "user",
            Content = "分析附件",
            Attachments = new[]
            {
                new ChatAttachment
                {
                    ConversationId = "chat-attachments",
                    FilePath = @"C:\chat\image.png",
                    FileName = "image.png",
                    MimeType = "image/png",
                    Sha256 = "sha",
                    ByteLength = 123,
                    CreatedAt = now
                }
            },
            CreatedAt = now
        });

        var message = Assert.Single(repository.ListMessages("chat-attachments"));
        Assert.Equal(messageId, message.Id);
        var attachment = Assert.Single(message.Attachments);
        Assert.Equal(messageId, attachment.MessageId);
        Assert.Equal("chat-attachments", attachment.ConversationId);
        Assert.Equal("image.png", attachment.FileName);
        Assert.Equal("image/png", attachment.MimeType);
        Assert.Equal(123, attachment.ByteLength);
    }

    [Fact]
    public void TouchConversation_moves_conversation_to_top_of_list()
    {
        var database = new SqliteDatabase(AppPaths.CreateForRoot(_root));
        new SqliteSchemaInitializer(database).Initialize();
        var repository = new ChatRepository(database);
        var now = DateTimeOffset.Parse("2026-06-06T12:00:00Z");
        repository.CreateConversation(new ChatConversation { Id = "old", Title = "旧", Model = "gpt-4o-mini", CreatedAt = now, UpdatedAt = now });
        repository.CreateConversation(new ChatConversation { Id = "new", Title = "新", Model = "gpt-4o-mini", CreatedAt = now.AddMinutes(1), UpdatedAt = now.AddMinutes(1) });

        repository.TouchConversation("old", now.AddMinutes(2));

        Assert.Equal("old", repository.ListConversations().First().Id);
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
