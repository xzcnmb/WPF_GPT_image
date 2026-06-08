using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Gpt2Image.Wpf.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly CreatePageViewModel _create;
    private readonly VideoGenerationPageViewModel _video;
    private readonly AgentPageViewModel _agent;
    private readonly ChatPageViewModel _chat;
    private readonly HistoryPageViewModel _history;
    private readonly SettingsPageViewModel _settings;

    [ObservableProperty]
    private object _currentPage;

    [ObservableProperty]
    private NavigationMenuItemViewModel? _selectedNavigationItem;

    [ObservableProperty]
    private string _statusText = "创作中心";

    public ObservableCollection<NavigationMenuItemViewModel> NavigationItems { get; } = new();
    public ObservableCollection<NavigationMenuItemViewModel> FooterNavigationItems { get; } = new();
    public ObservableCollection<QueueItemViewModel> QueueItems { get; } = new();

    public MainWindowViewModel(
        CreatePageViewModel create,
        VideoGenerationPageViewModel video,
        AgentPageViewModel agent,
        ChatPageViewModel chat,
        HistoryPageViewModel history,
        SettingsPageViewModel settings)
    {
        _create = create;
        _video = video;
        _agent = agent;
        _chat = chat;
        _history = history;
        _settings = settings;
        _currentPage = _create;

        NavigationItems.Add(new NavigationMenuItemViewModel("图片生成", "创作中心", "Image", _create));
        NavigationItems.Add(new NavigationMenuItemViewModel("视频生成", "创作中心", "Video", _video));
        NavigationItems.Add(new NavigationMenuItemViewModel("自动任务 / Goal", "自动化", "Robot", _agent));
        NavigationItems.Add(new NavigationMenuItemViewModel("对话", "工作区", "Chat", _chat, () => _chat.RefreshConversations()));
        NavigationItems.Add(new NavigationMenuItemViewModel("历史记录", "内容管理", "History", _history, () => _history.RefreshHistory()));
        NavigationItems.Add(new NavigationMenuItemViewModel("设置", "系统", "Cog", _settings));
        SelectedNavigationItem = NavigationItems.FirstOrDefault();
    }

    partial void OnSelectedNavigationItemChanged(NavigationMenuItemViewModel? value)
    {
        if (value is null)
        {
            return;
        }

        value.BeforeNavigate?.Invoke();
        CurrentPage = value.Page;
        StatusText = value.Group;
    }
}

public sealed class NavigationMenuItemViewModel
{
    public NavigationMenuItemViewModel(string title, string group, string icon, object page, Action? beforeNavigate = null)
    {
        Title = title;
        Group = group;
        Icon = icon;
        Page = page;
        BeforeNavigate = beforeNavigate;
    }

    public string Title { get; }
    public string Group { get; }
    public string Icon { get; }
    public object Page { get; }
    public Action? BeforeNavigate { get; }
}

public sealed record QueueItemViewModel(string Title, string Status);
