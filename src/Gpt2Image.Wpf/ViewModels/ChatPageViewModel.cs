using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gpt2Image.Core.Api;
using Gpt2Image.Core.Models;
using Gpt2Image.Core.Storage.Repositories;
using Microsoft.Extensions.Logging;

namespace Gpt2Image.Wpf.ViewModels;

public sealed partial class ChatPageViewModel : ObservableObject
{
    private const int ContextMessageLimit = 20;
    private readonly BackendProfileRepository _profiles;
    private readonly ChatRepository _chats;
    private readonly IImageGenerationClient _client;
    private readonly ILogger<ChatPageViewModel> _logger;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private string _input = "";

    [ObservableProperty]
    private string _status = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteConversationCommand))]
    private bool _isSending;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteConversationCommand))]
    private ChatConversationItemViewModel? _selectedConversation;

    public ChatPageViewModel(
        BackendProfileRepository profiles,
        ChatRepository chats,
        IImageGenerationClient client,
        ILogger<ChatPageViewModel> logger)
    {
        _profiles = profiles;
        _chats = chats;
        _client = client;
        _logger = logger;
        RefreshConversations();
    }

    public ObservableCollection<ChatConversationItemViewModel> Conversations { get; } = new();
    public ObservableCollection<ChatMessageViewModel> Messages { get; } = new();

    [RelayCommand]
    public void RefreshConversations()
    {
        var selectedId = SelectedConversation?.Id;
        Conversations.Clear();
        foreach (var conversation in _chats.ListConversations())
        {
            Conversations.Add(ChatConversationItemViewModel.FromModel(conversation));
        }

        if (!string.IsNullOrWhiteSpace(selectedId))
        {
            SelectedConversation = Conversations.FirstOrDefault(item => item.Id == selectedId);
        }
        else if (SelectedConversation is null && Conversations.Count > 0)
        {
            SelectedConversation = Conversations[0];
        }

        Status = Conversations.Count == 0 ? "暂无会话" : $"{Conversations.Count} 个会话";
    }

    [RelayCommand]
    private void NewConversation()
    {
        SelectedConversation = null;
        Messages.Clear();
        Input = "";
        Status = "新会话";
    }

    [RelayCommand(CanExecute = nameof(CanDeleteConversation))]
    private void DeleteConversation()
    {
        if (SelectedConversation is null)
        {
            return;
        }

        var id = SelectedConversation.Id;
        _chats.SoftDeleteConversation(id);
        Conversations.Remove(SelectedConversation);
        SelectedConversation = Conversations.FirstOrDefault();
        Status = "会话已删除";
    }

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync(CancellationToken cancellationToken)
    {
        var text = Input.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            Status = "请输入消息";
            return;
        }

        var profile = _profiles.ListEnabled().FirstOrDefault();
        if (profile is null)
        {
            Status = "缺少后端配置";
            return;
        }

        var conversation = EnsureConversation(profile, text);
        var now = DateTimeOffset.UtcNow;
        var userMessage = new ChatMessage
        {
            ConversationId = conversation.Id,
            Role = "user",
            Content = text,
            CreatedAt = now
        };

        _chats.AddMessage(userMessage);
        _chats.TouchConversation(conversation.Id, now);
        Messages.Add(ChatMessageViewModel.FromModel(userMessage));
        Input = "";

        try
        {
            IsSending = true;
            Status = "发送中";
            var messages = _chats.ListMessages(conversation.Id)
                .TakeLast(ContextMessageLimit)
                .ToList();
            var result = await _client.ChatAsync(profile, new ChatRequest
            {
                ConversationId = conversation.Id,
                Model = conversation.Model,
                Messages = messages
            }, cancellationToken);

            if (!string.IsNullOrWhiteSpace(result.Error))
            {
                Status = $"发送失败：{result.Error}";
                _logger.LogWarning("聊天失败：{Error}", result.Error);
                return;
            }

            if (string.IsNullOrWhiteSpace(result.Content))
            {
                Status = "后端返回空回复";
                return;
            }

            var assistantMessage = new ChatMessage
            {
                ConversationId = conversation.Id,
                Role = "assistant",
                Content = result.Content.Trim(),
                RawJson = result.RawJson,
                CreatedAt = DateTimeOffset.UtcNow
            };
            _chats.AddMessage(assistantMessage);
            _chats.TouchConversation(conversation.Id, assistantMessage.CreatedAt);
            Messages.Add(ChatMessageViewModel.FromModel(assistantMessage));
            Status = "已回复";
            RefreshConversationItem(conversation.Id, assistantMessage.CreatedAt);
        }
        catch (OperationCanceledException)
        {
            Status = "发送已取消";
        }
        catch (Exception ex)
        {
            Status = $"发送异常：{ex.Message}";
            _logger.LogError(ex, "聊天发送异常");
        }
        finally
        {
            IsSending = false;
        }
    }

    partial void OnSelectedConversationChanged(ChatConversationItemViewModel? value)
    {
        Messages.Clear();
        if (value is null)
        {
            Status = "新会话";
            return;
        }

        foreach (var message in _chats.ListMessages(value.Id))
        {
            Messages.Add(ChatMessageViewModel.FromModel(message));
        }

        Status = $"已加载 {Messages.Count} 条消息";
    }

    private bool CanSend() => !IsSending && !string.IsNullOrWhiteSpace(Input);

    private bool CanDeleteConversation() => !IsSending && SelectedConversation is not null;

    private ChatConversationItemViewModel EnsureConversation(BackendProfile profile, string firstMessage)
    {
        if (SelectedConversation is not null)
        {
            return SelectedConversation;
        }

        var now = DateTimeOffset.UtcNow;
        var conversation = new ChatConversation
        {
            Id = Guid.NewGuid().ToString("N"),
            Title = BuildTitle(firstMessage),
            BackendProfileId = profile.Id,
            Model = string.IsNullOrWhiteSpace(profile.MainlineModel) ? profile.ImageModel : profile.MainlineModel,
            CreatedAt = now,
            UpdatedAt = now
        };
        _chats.CreateConversation(conversation);
        var item = ChatConversationItemViewModel.FromModel(conversation);
        Conversations.Insert(0, item);
        SelectedConversation = item;
        return item;
    }

    private void RefreshConversationItem(string conversationId, DateTimeOffset updatedAt)
    {
        var item = Conversations.FirstOrDefault(conversation => conversation.Id == conversationId);
        if (item is null)
        {
            RefreshConversations();
            return;
        }

        item.UpdatedAt = updatedAt;
        item.UpdatedAtText = updatedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm");
        var index = Conversations.IndexOf(item);
        if (index > 0)
        {
            Conversations.Move(index, 0);
        }
    }

    private static string BuildTitle(string text)
    {
        var title = text.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal).Trim();
        return title.Length <= 28 ? title : title[..28] + "...";
    }
}

public sealed partial class ChatConversationItemViewModel : ObservableObject
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string Model { get; init; } = "";

    [ObservableProperty]
    private DateTimeOffset _updatedAt;

    [ObservableProperty]
    private string _updatedAtText = "";

    public static ChatConversationItemViewModel FromModel(ChatConversation conversation) => new()
    {
        Id = conversation.Id,
        Title = conversation.Title,
        Model = conversation.Model,
        UpdatedAt = conversation.UpdatedAt,
        UpdatedAtText = conversation.UpdatedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm")
    };
}

public sealed class ChatMessageViewModel
{
    public string Role { get; init; } = "";
    public string RoleText => string.Equals(Role, "user", StringComparison.OrdinalIgnoreCase) ? "我" : "助手";
    public string Content { get; init; } = "";
    public string CreatedAtText { get; init; } = "";
    public bool IsUser => string.Equals(Role, "user", StringComparison.OrdinalIgnoreCase);

    public static ChatMessageViewModel FromModel(ChatMessage message) => new()
    {
        Role = message.Role,
        Content = message.Content,
        CreatedAtText = message.CreatedAt.LocalDateTime.ToString("HH:mm")
    };
}
