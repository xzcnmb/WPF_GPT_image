using Gpt2Image.Core.Api;
using Gpt2Image.Core.Models;
using Gpt2Image.Core.Security;
using Gpt2Image.Core.Storage;
using Gpt2Image.Core.Storage.Repositories;
using Gpt2Image.Wpf.ViewModels;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace Gpt2Image.Tests.Wpf;

public sealed class ChatPageViewModelTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "gpt2image-chat-vm-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SendAsync_with_new_conversation_persists_user_and_assistant_messages_and_code_blocks()
    {
        var fixture = CreateFixture("助手回复\n```csharp\npublic static int Add(int a, int b) => a + b;\n```");
        var viewModel = fixture.ViewModel;
        viewModel.Input = "写一段 C# 代码";

        await viewModel.SendCommand.ExecuteAsync(null);

        Assert.Equal("已回复", viewModel.Status);
        Assert.Equal(2, viewModel.Messages.Count);
        Assert.Equal("我", viewModel.Messages[0].RoleText);
        var assistant = viewModel.Messages[1];
        Assert.Contains("助手回复", assistant.Content);
        Assert.Contains(assistant.Blocks, block => block.IsCode && block.Language == "csharp" && block.Text.Contains("Add"));
        var conversation = Assert.Single(fixture.Chats.ListConversations());
        Assert.Equal("chat-default", conversation.BackendProfileId);
        Assert.Equal("gpt-4o-mini", conversation.Model);
        Assert.Equal(2, fixture.Chats.ListMessages(conversation.Id).Count);
        Assert.Equal("gpt-4o-mini", fixture.Client.LastRequest?.Model);
    }

    [Fact]
    public async Task SendAsync_with_existing_gpt55_conversation_sends_normalized_model()
    {
        var fixture = CreateFixture("助手回复");
        var now = DateTimeOffset.Parse("2026-06-06T12:00:00Z");
        fixture.Chats.CreateConversation(new ChatConversation
        {
            Id = "existing-chat",
            Title = "旧会话",
            BackendProfileId = "chat-default",
            Model = "gpt-5.5",
            CreatedAt = now,
            UpdatedAt = now
        });
        fixture.ViewModel.RefreshConversations();
        fixture.ViewModel.SelectedConversation = fixture.ViewModel.Conversations.Single(item => item.Id == "existing-chat");
        fixture.ViewModel.Input = "继续";

        await fixture.ViewModel.SendCommand.ExecuteAsync(null);

        Assert.Equal("gpt-4o-mini", fixture.Client.LastRequest?.Model);
    }

    [Fact]
    public async Task SendAsync_when_client_returns_error_persists_visible_error_message()
    {
        var fixture = CreateFixture(error: "模型不可用");
        var viewModel = fixture.ViewModel;
        viewModel.Input = "你是谁";

        await viewModel.SendCommand.ExecuteAsync(null);

        Assert.StartsWith("发送失败：", viewModel.Status);
        Assert.Equal(2, viewModel.Messages.Count);
        Assert.True(viewModel.Messages[1].IsError);
        Assert.Contains("模型不可用", viewModel.Messages[1].Content);
        var conversation = Assert.Single(fixture.Chats.ListConversations());
        var stored = fixture.Chats.ListMessages(conversation.Id);
        Assert.Equal("error", stored[1].Role);
    }

    [Fact]
    public async Task SendAsync_with_pending_attachment_persists_attachment_and_sends_it_to_client()
    {
        var fixture = CreateFixture("已分析");
        var viewModel = fixture.ViewModel;
        var now = DateTimeOffset.Parse("2026-06-06T12:00:00Z");
        viewModel.PendingAttachments.Add(new ChatAttachmentViewModel
        {
            FilePath = CreateTempFile("note.txt", "hello"),
            FileName = "note.txt",
            MimeType = "text/plain",
            Sha256 = "sha",
            ByteLength = 5
        });
        viewModel.Input = "分析这个文件";

        await viewModel.SendCommand.ExecuteAsync(null);

        Assert.Empty(viewModel.PendingAttachments);
        Assert.Single(viewModel.Messages[0].Attachments);
        Assert.Single(fixture.Client.LastRequest?.Messages.Last().Attachments ?? Array.Empty<ChatAttachment>());
        var conversation = Assert.Single(fixture.Chats.ListConversations());
        Assert.Single(fixture.Chats.ListMessages(conversation.Id).First().Attachments);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private Fixture CreateFixture(string content = "助手回复", string? error = null)
    {
        var paths = AppPaths.CreateForRoot(_root);
        var database = new SqliteDatabase(paths);
        new SqliteSchemaInitializer(database).Initialize();
        var profiles = new BackendProfileRepository(database, new PassThroughSecretProtector());
        profiles.Upsert(new BackendProfile
        {
            Id = "chat-default",
            Name = "聊天 API",
            BaseUrl = "https://example.test/v1",
            ApiKey = "sk-test",
            Protocol = BackendProtocol.ChatCompletionsImageJson,
            MainlineModel = "gpt-4o-mini",
            ImageModel = "gpt-image-2",
            Concurrency = 1,
            Priority = 0,
            IsEnabled = true,
            SupportsPromptOptimization = false,
            SupportsChat = true,
            SupportsImageGeneration = false,
            SupportsVideoGeneration = false,
            SupportsAgent = false
        });
        var chats = new ChatRepository(database);
        var client = new StubChatClient(content, error);
        var viewModel = new ChatPageViewModel(
            profiles,
            chats,
            new LocalImageStorage(paths, new FixedClock(new DateTimeOffset(2026, 6, 6, 12, 0, 0, TimeSpan.Zero))),
            paths,
            client,
            NullLogger<ChatPageViewModel>.Instance);
        return new Fixture(chats, client, viewModel);
    }

    private string CreateTempFile(string fileName, string content)
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, fileName);
        File.WriteAllText(path, content);
        return path;
    }

    private sealed record Fixture(ChatRepository Chats, StubChatClient Client, ChatPageViewModel ViewModel);

    private sealed class StubChatClient : IImageGenerationClient
    {
        private readonly string _content;
        private readonly string? _error;

        public StubChatClient(string content, string? error)
        {
            _content = content;
            _error = error;
        }

        public ChatRequest? LastRequest { get; private set; }

        public Task<GenerationResult> GenerateAsync(BackendProfile profile, ImageGenerationRequest request, CancellationToken cancellationToken)
            => Task.FromResult(new GenerationResult());

        public Task<ChatResult> ChatAsync(BackendProfile profile, ChatRequest request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(string.IsNullOrWhiteSpace(_error)
                ? new ChatResult { Content = _content, RawJson = "{\"ok\":true}" }
                : new ChatResult { Error = _error });
        }

        public async IAsyncEnumerable<ImageStreamEvent> StreamAgentImagesAsync(
            BackendProfile profile,
            AgentRunRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class FixedClock : IClock
    {
        private readonly DateTimeOffset _now;

        public FixedClock(DateTimeOffset now)
        {
            _now = now;
        }

        public DateTimeOffset UtcNow => _now;
    }

    private sealed class PassThroughSecretProtector : ISecretProtector
    {
        public string Protect(string value) => value;

        public string Unprotect(string protectedValue) => protectedValue;
    }
}
