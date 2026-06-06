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

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
