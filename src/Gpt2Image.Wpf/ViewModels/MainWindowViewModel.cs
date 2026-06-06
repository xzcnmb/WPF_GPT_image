using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Gpt2Image.Wpf.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly CreatePageViewModel _create;
    private readonly AgentPageViewModel _agent;
    private readonly ChatPageViewModel _chat;
    private readonly HistoryPageViewModel _history;
    private readonly SettingsPageViewModel _settings;

    [ObservableProperty]
    private object _currentPage;

    [ObservableProperty]
    private string _statusText = "";

    public ObservableCollection<QueueItemViewModel> QueueItems { get; } = new();

    public MainWindowViewModel(
        CreatePageViewModel create,
        AgentPageViewModel agent,
        ChatPageViewModel chat,
        HistoryPageViewModel history,
        SettingsPageViewModel settings)
    {
        _create = create;
        _agent = agent;
        _chat = chat;
        _history = history;
        _settings = settings;
        _currentPage = _create;
    }

    [RelayCommand]
    private void ShowCreate() => CurrentPage = _create;

    [RelayCommand]
    private void ShowAgent() => CurrentPage = _agent;

    [RelayCommand]
    private void ShowChat()
    {
        _chat.RefreshConversations();
        CurrentPage = _chat;
    }

    [RelayCommand]
    private void ShowHistory()
    {
        _history.RefreshHistory();
        CurrentPage = _history;
    }

    [RelayCommand]
    private void ShowSettings() => CurrentPage = _settings;
}

public sealed record QueueItemViewModel(string Title, string Status);
