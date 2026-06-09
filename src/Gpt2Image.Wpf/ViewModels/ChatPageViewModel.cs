using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gpt2Image.Core.Api;
using Gpt2Image.Core.Models;
using Gpt2Image.Core.Storage;
using Gpt2Image.Core.Storage.Repositories;
using Microsoft.Extensions.Logging;
using Clipboard = System.Windows.Clipboard;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace Gpt2Image.Wpf.ViewModels;

public sealed partial class ChatPageViewModel : ObservableObject
{
    private const int ContextMessageLimit = 20;
    private const long MaxAttachmentBytes = 20L * 1024 * 1024;
    private readonly BackendProfileRepository _profiles;
    private readonly ChatRepository _chats;
    private readonly LocalImageStorage _images;
    private readonly AppPaths _paths;
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

    [ObservableProperty]
    private BackendProfileItemViewModel? _selectedChatProfile;

    public ChatPageViewModel(
        BackendProfileRepository profiles,
        ChatRepository chats,
        LocalImageStorage images,
        AppPaths paths,
        IImageGenerationClient client,
        ILogger<ChatPageViewModel> logger)
    {
        _profiles = profiles;
        _chats = chats;
        _images = images;
        _paths = paths;
        _client = client;
        _logger = logger;
        PendingAttachments.CollectionChanged += OnPendingAttachmentsChanged;
        RefreshChatProfiles();
        RefreshConversations();
    }

    public ObservableCollection<ChatConversationItemViewModel> Conversations { get; } = new();
    public ObservableCollection<ChatMessageViewModel> Messages { get; } = new();
    public ObservableCollection<ChatAttachmentViewModel> PendingAttachments { get; } = new();
    public ObservableCollection<BackendProfileItemViewModel> AvailableChatProfiles { get; } = new();

    [RelayCommand]
    public void RefreshChatProfiles()
    {
        var selectedId = SelectedChatProfile?.Id;
        AvailableChatProfiles.Clear();
        foreach (var profile in _profiles.ListEnabledForRole(BackendProfileRole.Chat))
        {
            AvailableChatProfiles.Add(BackendProfileItemViewModel.FromProfile(profile));
        }

        SelectedChatProfile = !string.IsNullOrWhiteSpace(selectedId)
            ? AvailableChatProfiles.FirstOrDefault(item => item.Id == selectedId) ?? AvailableChatProfiles.FirstOrDefault()
            : AvailableChatProfiles.FirstOrDefault();
    }

    [RelayCommand]
    public void RefreshConversations()
    {
        RefreshChatProfiles();
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
        PendingAttachments.Clear();
        Input = "";
        Status = "新会话";
    }

    [RelayCommand]
    private void AddAttachments()
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择聊天附件",
            Filter = "支持的附件|*.png;*.jpg;*.jpeg;*.webp;*.txt;*.md;*.json;*.xml;*.csv;*.log;*.cs;*.js;*.ts;*.py;*.java;*.cpp;*.h;*.html;*.css;*.sql|图片|*.png;*.jpg;*.jpeg;*.webp|文本/代码|*.txt;*.md;*.json;*.xml;*.csv;*.log;*.cs;*.js;*.ts;*.py;*.java;*.cpp;*.h;*.html;*.css;*.sql|所有文件|*.*",
            Multiselect = true,
            CheckFileExists = true
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        foreach (var fileName in dialog.FileNames)
        {
            try
            {
                PendingAttachments.Add(SaveAttachment(fileName));
            }
            catch (Exception ex)
            {
                Status = $"添加附件失败：{ex.Message}";
                _logger.LogWarning(ex, "添加聊天附件失败：{FileName}", fileName);
            }
        }
    }

    [RelayCommand]
    private void RemoveAttachment(ChatAttachmentViewModel? attachment)
    {
        if (attachment is not null)
        {
            PendingAttachments.Remove(attachment);
        }
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

    [RelayCommand(CanExecute = nameof(CanSend), IncludeCancelCommand = true)]
    private async Task SendAsync(CancellationToken cancellationToken)
    {
        var text = Input.Trim();
        if (string.IsNullOrWhiteSpace(text) && PendingAttachments.Count == 0)
        {
            Status = "请输入消息或添加附件";
            return;
        }

        var profile = ResolveProfileForSend();
        if (profile is null)
        {
            Status = "缺少聊天后端配置";
            return;
        }

        var conversation = EnsureConversation(profile, string.IsNullOrWhiteSpace(text) ? PendingAttachments.First().FileName : text);
        var now = DateTimeOffset.UtcNow;
        var attachments = PendingAttachments.Select(item => item.ToModel(conversation.Id, now)).ToList();
        var userMessage = new ChatMessage
        {
            ConversationId = conversation.Id,
            Role = "user",
            Content = text,
            Attachments = attachments,
            CreatedAt = now
        };

        var userMessageId = _chats.AddMessage(userMessage);
        var persistedUserMessage = new ChatMessage
        {
            Id = userMessageId,
            ConversationId = userMessage.ConversationId,
            Role = userMessage.Role,
            Content = userMessage.Content,
            Attachments = attachments.Select(attachment => new ChatAttachment
            {
                Id = attachment.Id,
                MessageId = userMessageId,
                ConversationId = conversation.Id,
                FilePath = attachment.FilePath,
                FileName = attachment.FileName,
                MimeType = attachment.MimeType,
                Sha256 = attachment.Sha256,
                ByteLength = attachment.ByteLength,
                CreatedAt = attachment.CreatedAt
            }).ToList(),
            CreatedAt = userMessage.CreatedAt
        };
        _chats.TouchConversation(conversation.Id, now);
        Messages.Add(ChatMessageViewModel.FromModel(persistedUserMessage));
        PendingAttachments.Clear();
        Input = "";

        try
        {
            IsSending = true;
            Status = "发送中";
            var messages = _chats.ListMessages(conversation.Id)
                .Where(message => !string.Equals(message.Role, "error", StringComparison.OrdinalIgnoreCase))
                .TakeLast(ContextMessageLimit)
                .ToList();
            var result = await _client.ChatAsync(profile, new ChatRequest
            {
                ConversationId = conversation.Id,
                Model = NormalizeConversationModel(string.IsNullOrWhiteSpace(conversation.Model) ? profile.MainlineModel : conversation.Model),
                Messages = messages
            }, cancellationToken);

            if (!string.IsNullOrWhiteSpace(result.Error))
            {
                AddErrorMessage(conversation.Id, result.Error!);
                Status = $"发送失败：{result.Error}";
                _logger.LogWarning("聊天失败：{Error}", result.Error);
                return;
            }

            if (string.IsNullOrWhiteSpace(result.Content))
            {
                const string error = "后端返回空回复";
                AddErrorMessage(conversation.Id, error);
                Status = error;
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
            var assistantMessageId = _chats.AddMessage(assistantMessage);
            assistantMessage = new ChatMessage
            {
                Id = assistantMessageId,
                ConversationId = assistantMessage.ConversationId,
                Role = assistantMessage.Role,
                Content = assistantMessage.Content,
                RawJson = assistantMessage.RawJson,
                CreatedAt = assistantMessage.CreatedAt
            };
            _chats.TouchConversation(conversation.Id, assistantMessage.CreatedAt);
            Messages.Add(ChatMessageViewModel.FromModel(assistantMessage));
            Status = "已回复";
            RefreshConversationItem(conversation.Id, assistantMessage.CreatedAt);
        }
        catch (OperationCanceledException)
        {
            Status = "发送已取消";
            AddErrorMessage(conversation.Id, "发送已取消");
        }
        catch (Exception ex)
        {
            Status = $"发送异常：{ex.Message}";
            AddErrorMessage(conversation.Id, ex.Message);
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

    private bool CanSend() => !IsSending && (!string.IsNullOrWhiteSpace(Input) || PendingAttachments.Count > 0);

    private bool CanDeleteConversation() => !IsSending && SelectedConversation is not null;

    private BackendProfile? ResolveProfileForSend()
    {
        if (!string.IsNullOrWhiteSpace(SelectedChatProfile?.Id))
        {
            var selectedProfile = _profiles.GetById(SelectedChatProfile.Id);
            if (selectedProfile is not null && IsChatUsableProfile(selectedProfile))
            {
                return selectedProfile;
            }
        }

        if (!string.IsNullOrWhiteSpace(SelectedConversation?.BackendProfileId))
        {
            var existingProfile = _profiles.GetById(SelectedConversation.BackendProfileId);
            if (existingProfile is not null && IsChatUsableProfile(existingProfile))
            {
                return existingProfile;
            }
        }

        return _profiles.GetFirstEnabledForRole(BackendProfileRole.Chat);
    }

    private static bool IsChatUsableProfile(BackendProfile profile)
    {
        return profile.IsEnabled
               && profile.SupportsChat
               && BackendProtocol.SupportsChat(profile.Protocol);
    }

    private ChatAttachmentViewModel SaveAttachment(string sourcePath)
    {
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("附件不存在。", sourcePath);
        }

        var info = new FileInfo(sourcePath);
        if (info.Length > MaxAttachmentBytes)
        {
            throw new InvalidOperationException($"附件超过 20MB：{info.Name}");
        }

        if (IsSupportedImage(sourcePath))
        {
            var saved = _images.SaveInputAsset(sourcePath);
            return new ChatAttachmentViewModel
            {
                FilePath = saved.FilePath,
                FileName = Path.GetFileName(sourcePath),
                MimeType = saved.MimeType,
                Sha256 = saved.Sha256,
                ByteLength = saved.ByteLength
            };
        }

        if (!IsSupportedText(sourcePath))
        {
            throw new InvalidOperationException("当前聊天附件支持图片和文本/代码文件。");
        }

        _paths.EnsureDirectories();
        var now = DateTimeOffset.UtcNow;
        var directory = Path.Combine(_paths.ChatAttachmentsDirectory, now.ToString("yyyy"), now.ToString("MM"), now.ToString("dd"));
        Directory.CreateDirectory(directory);
        var fileName = $"{now:HHmmss}_{Guid.NewGuid():N}{Path.GetExtension(sourcePath)}";
        var targetPath = Path.Combine(directory, fileName);
        File.Copy(sourcePath, targetPath, overwrite: false);
        var bytes = File.ReadAllBytes(targetPath);
        return new ChatAttachmentViewModel
        {
            FilePath = targetPath,
            FileName = Path.GetFileName(sourcePath),
            MimeType = GuessTextMimeType(sourcePath),
            Sha256 = Convert.ToHexString(SHA256.HashData(bytes)),
            ByteLength = bytes.LongLength
        };
    }

    private void AddErrorMessage(string conversationId, string error)
    {
        var now = DateTimeOffset.UtcNow;
        var message = new ChatMessage
        {
            ConversationId = conversationId,
            Role = "error",
            Content = $"请求失败：{error}",
            CreatedAt = now
        };
        var messageId = _chats.AddMessage(message);
        Messages.Add(ChatMessageViewModel.FromModel(new ChatMessage
        {
            Id = messageId,
            ConversationId = conversationId,
            Role = "error",
            Content = message.Content,
            CreatedAt = now
        }));
    }

    private ChatConversationItemViewModel EnsureConversation(BackendProfile profile, string firstMessage)
    {
        if (SelectedConversation is not null
            && string.Equals(SelectedConversation.BackendProfileId, profile.Id, StringComparison.OrdinalIgnoreCase))
        {
            return SelectedConversation;
        }

        if (SelectedConversation is not null)
        {
            Messages.Clear();
            Status = $"已切换到 {profile.Name}，自动创建新会话。";
        }

        var now = DateTimeOffset.UtcNow;
        var conversation = new ChatConversation
        {
            Id = Guid.NewGuid().ToString("N"),
            Title = BuildTitle(firstMessage),
            BackendProfileId = profile.Id,
            Model = NormalizeConversationModel(profile.MainlineModel),
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

    private void OnPendingAttachmentsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        SendCommand.NotifyCanExecuteChanged();
    }

    private static string BuildTitle(string text)
    {
        var title = text.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal).Trim();
        return title.Length <= 28 ? title : title[..28] + "...";
    }

    private static string NormalizeConversationModel(string? model)
    {
        var value = model?.Trim();
        return string.IsNullOrWhiteSpace(value) || string.Equals(value, "gpt-5.5", StringComparison.OrdinalIgnoreCase)
            ? "gpt-4o-mini"
            : value;
    }

    private static bool IsSupportedImage(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".webp";
    }

    private static bool IsSupportedText(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() is ".txt" or ".md" or ".json" or ".xml" or ".csv" or ".log" or ".cs" or ".js" or ".ts" or ".py" or ".java" or ".cpp" or ".h" or ".html" or ".css" or ".sql";
    }

    private static string GuessTextMimeType(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".json" => "application/json",
            ".xml" => "application/xml",
            ".js" => "application/javascript",
            ".html" => "text/html",
            ".css" => "text/css",
            ".csv" => "text/csv",
            ".md" => "text/markdown",
            _ => "text/plain"
        };
    }
}

public sealed partial class ChatConversationItemViewModel : ObservableObject
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string? BackendProfileId { get; init; }
    public string Model { get; init; } = "";

    [ObservableProperty]
    private DateTimeOffset _updatedAt;

    [ObservableProperty]
    private string _updatedAtText = "";

    public static ChatConversationItemViewModel FromModel(ChatConversation conversation) => new()
    {
        Id = conversation.Id,
        Title = conversation.Title,
        BackendProfileId = conversation.BackendProfileId,
        Model = conversation.Model,
        UpdatedAt = conversation.UpdatedAt,
        UpdatedAtText = conversation.UpdatedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm")
    };
}

public sealed class ChatMessageViewModel
{
    public string Role { get; init; } = "";
    public string RoleText => string.Equals(Role, "user", StringComparison.OrdinalIgnoreCase)
        ? "我"
        : string.Equals(Role, "error", StringComparison.OrdinalIgnoreCase) ? "错误" : "助手";
    public string Content { get; init; } = "";
    public IReadOnlyList<ChatAttachmentViewModel> Attachments { get; init; } = Array.Empty<ChatAttachmentViewModel>();
    public IReadOnlyList<ChatMessageContentBlockViewModel> Blocks { get; init; } = Array.Empty<ChatMessageContentBlockViewModel>();
    public string CreatedAtText { get; init; } = "";
    public bool IsUser => string.Equals(Role, "user", StringComparison.OrdinalIgnoreCase);
    public bool IsError => string.Equals(Role, "error", StringComparison.OrdinalIgnoreCase);
    public bool HasAttachments => Attachments.Count > 0;

    public static ChatMessageViewModel FromModel(ChatMessage message) => new()
    {
        Role = message.Role,
        Content = message.Content,
        Attachments = message.Attachments.Select(ChatAttachmentViewModel.FromModel).ToList(),
        Blocks = ChatMarkdownParser.Parse(message.Content),
        CreatedAtText = message.CreatedAt.LocalDateTime.ToString("HH:mm")
    };
}

public sealed class ChatAttachmentViewModel
{
    public string FilePath { get; init; } = "";
    public string FileName { get; init; } = "";
    public string MimeType { get; init; } = "";
    public string Sha256 { get; init; } = "";
    public long ByteLength { get; init; }
    public string SizeText => ByteLength <= 0 ? "" : $"{ByteLength / 1024d / 1024d:0.##} MB";
    public string PreviewSource => FilePath;
    public bool IsImage => MimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
    public bool IsText => MimeType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
                          || MimeType is "application/json" or "application/xml" or "application/javascript";

    public ChatAttachment ToModel(string conversationId, DateTimeOffset createdAt) => new()
    {
        ConversationId = conversationId,
        FilePath = FilePath,
        FileName = FileName,
        MimeType = MimeType,
        Sha256 = Sha256,
        ByteLength = ByteLength,
        CreatedAt = createdAt
    };

    public static ChatAttachmentViewModel FromModel(ChatAttachment attachment) => new()
    {
        FilePath = attachment.FilePath,
        FileName = string.IsNullOrWhiteSpace(attachment.FileName) ? Path.GetFileName(attachment.FilePath) : attachment.FileName,
        MimeType = attachment.MimeType,
        Sha256 = attachment.Sha256,
        ByteLength = attachment.ByteLength
    };
}

public sealed partial class ChatMessageContentBlockViewModel : ObservableObject
{
    public string Kind { get; init; } = "text";
    public string Text { get; init; } = "";
    public string Language { get; init; } = "";
    public bool IsCode => string.Equals(Kind, "code", StringComparison.OrdinalIgnoreCase);

    [RelayCommand]
    private void Copy()
    {
        if (!string.IsNullOrEmpty(Text))
        {
            Clipboard.SetText(Text);
        }
    }
}

public static class ChatMarkdownParser
{
    public static IReadOnlyList<ChatMessageContentBlockViewModel> Parse(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return Array.Empty<ChatMessageContentBlockViewModel>();
        }

        var blocks = new List<ChatMessageContentBlockViewModel>();
        using var reader = new StringReader(content);
        var buffer = new StringBuilder();
        var code = new StringBuilder();
        var inCode = false;
        var language = "";
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                if (!inCode)
                {
                    FlushText(blocks, buffer);
                    inCode = true;
                    language = line.Trim()[3..].Trim();
                }
                else
                {
                    blocks.Add(new ChatMessageContentBlockViewModel
                    {
                        Kind = "code",
                        Text = code.ToString().TrimEnd('\r', '\n'),
                        Language = language
                    });
                    code.Clear();
                    language = "";
                    inCode = false;
                }

                continue;
            }

            if (inCode)
            {
                code.AppendLine(line);
            }
            else
            {
                buffer.AppendLine(line);
            }
        }

        if (inCode)
        {
            blocks.Add(new ChatMessageContentBlockViewModel
            {
                Kind = "code",
                Text = code.ToString().TrimEnd('\r', '\n'),
                Language = language
            });
        }
        else
        {
            FlushText(blocks, buffer);
        }

        return blocks.Count == 0
            ? new[] { new ChatMessageContentBlockViewModel { Kind = "text", Text = content } }
            : blocks;
    }

    private static void FlushText(ICollection<ChatMessageContentBlockViewModel> blocks, StringBuilder buffer)
    {
        var text = buffer.ToString().TrimEnd('\r', '\n');
        if (!string.IsNullOrWhiteSpace(text))
        {
            blocks.Add(new ChatMessageContentBlockViewModel { Kind = "text", Text = text });
        }

        buffer.Clear();
    }
}
