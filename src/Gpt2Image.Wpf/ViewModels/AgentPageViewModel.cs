using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gpt2Image.Core.Api;
using Gpt2Image.Core.Models;
using Gpt2Image.Core.Storage.Repositories;

namespace Gpt2Image.Wpf.ViewModels;

public sealed partial class AgentPageViewModel : ObservableObject
{
    private readonly BackendProfileRepository _profiles;
    private readonly IImageGenerationClient _client;

    [ObservableProperty]
    private string _goal = "";

    [ObservableProperty]
    private int _maxRounds = 5;

    [ObservableProperty]
    private bool _useWebSearch = true;

    [ObservableProperty]
    private string _status = "";

    public AgentPageViewModel(
        BackendProfileRepository profiles,
        IImageGenerationClient client)
    {
        _profiles = profiles;
        _client = client;
    }

    [RelayCommand]
    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var profile = _profiles.GetFirstEnabledForRole(BackendProfileRole.Agent);
        if (profile is null)
        {
            Status = "缺少 Agent / Responses 后端配置";
            return;
        }

        Status = "运行中";
        var count = 0;
        await foreach (var item in _client.StreamAgentImagesAsync(profile, new AgentRunRequest
        {
            Goal = Goal,
            MaxRounds = MaxRounds,
            UseWebSearch = UseWebSearch
        }, cancellationToken))
        {
            count++;
            Status = item.Kind switch
            {
                ImageStreamEventKind.PartialImage => $"局部图 #{item.PartialImageIndex ?? 0}",
                ImageStreamEventKind.FinalImage => "最终图",
                ImageStreamEventKind.Completed => $"完成：{item.ResponseId}",
                ImageStreamEventKind.Error => $"失败：{item.Error}",
                _ => $"事件 {count}"
            };
        }
    }
}
