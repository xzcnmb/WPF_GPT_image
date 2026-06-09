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
    private readonly CodingPageViewModel _coding;
    private readonly ChatPageViewModel _chat;
    private readonly HistoryPageViewModel _history;
    private readonly SettingsPageViewModel _settings;

    [ObservableProperty]
    private object _currentPage;

    [ObservableProperty]
    private NavigationMenuItemViewModel? _selectedNavigationItem;

    [ObservableProperty]
    private string _statusText = "工作台";

    public ObservableCollection<NavigationMenuItemViewModel> NavigationItems { get; } = new();
    public ObservableCollection<NavigationMenuItemViewModel> FooterNavigationItems { get; } = new();
    public ObservableCollection<QueueItemViewModel> QueueItems { get; } = new();

    public MainWindowViewModel(
        CreatePageViewModel create,
        VideoGenerationPageViewModel video,
        AgentPageViewModel agent,
        CodingPageViewModel coding,
        ChatPageViewModel chat,
        HistoryPageViewModel history,
        SettingsPageViewModel settings)
    {
        _create = create;
        _video = video;
        _agent = agent;
        _coding = coding;
        _chat = chat;
        _history = history;
        _settings = settings;
        _currentPage = _create;

        NavigationItems.Add(new NavigationMenuItemViewModel("图像创作", "内容创作", "Image", _create));
        NavigationItems.Add(new NavigationMenuItemViewModel("视频创作", "内容创作", "Video", _video));
        NavigationItems.Add(new NavigationMenuItemViewModel("对话", "协作", "Chat", _chat, () => _chat.RefreshConversations()));
        NavigationItems.Add(new NavigationMenuItemViewModel("任务", "自动化", "Robot", _agent));
        NavigationItems.Add(new NavigationMenuItemViewModel("编码工作台", "开发", "CodeBraces", _coding));
        NavigationItems.Add(new NavigationMenuItemViewModel("历史与资产", "资产管理", "History", _history, () => _history.RefreshHistory()));
        FooterNavigationItems.Add(new NavigationMenuItemViewModel("模型与设置", "系统设置", "Cog", _settings));
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
