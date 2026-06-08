using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gpt2Image.Core.Models;
using Gpt2Image.Core.Storage.Repositories;

namespace Gpt2Image.Wpf.ViewModels;

public sealed partial class SettingsPageViewModel : ObservableObject
{
    private const string LegacyDefaultProfileId = "default";
    private const string LegacyRoutinVideoProfileId = "routin-video";
    private const string DefaultMainlineModel = "gpt-4o-mini";
    private const string DefaultRoutinBaseUrl = "https://api.routin.ai";
    private const string DefaultRoutinVideoModel = "grok-imagine-video";
    private readonly BackendProfileRepository _profiles;
    private bool _isLoadingProfile;

    [ObservableProperty]
    private string _selectedProfileRole = BackendProfileRole.Image;

    [ObservableProperty]
    private string _name = "图片生成 API";

    [ObservableProperty]
    private string _baseUrl = "https://api.openai.com/v1";

    [ObservableProperty]
    private string _apiKey = "";

    [ObservableProperty]
    private string _selectedProtocol = BackendProtocol.OpenAiImages;

    [ObservableProperty]
    private string _mainlineModel = DefaultMainlineModel;

    [ObservableProperty]
    private string _imageModel = "gpt-image-2";

    [ObservableProperty]
    private string _videoModel = DefaultRoutinVideoModel;

    [ObservableProperty]
    private int _concurrency = 1;

    [ObservableProperty]
    private string _status = "";

    public SettingsPageViewModel(BackendProfileRepository profiles)
    {
        _profiles = profiles;
        LoadProfileForRole(SelectedProfileRole);
    }

    public IReadOnlyList<ProfileRoleOptionViewModel> RoleOptions { get; } = BackendProfileRole.KnownValues
        .Select(value => new ProfileRoleOptionViewModel(value, BackendProfileRole.DisplayName(value), DescriptionForRole(value)))
        .ToList();

    public IReadOnlyList<ProtocolOptionViewModel> ProtocolOptions { get; } = BackendProtocol.KnownValues
        .Select(value => new ProtocolOptionViewModel(value, BackendProtocol.DisplayName(value), BackendProtocol.Description(value)))
        .ToList();

    public string SelectedProfileRoleDescription => RoleOptions.FirstOrDefault(item => item.Value == BackendProfileRole.Normalize(SelectedProfileRole))?.Description ?? "";

    public string SelectedProtocolDescription => BackendProtocol.Description(SelectedProtocol);

    [RelayCommand]
    private void Save()
    {
        if (string.IsNullOrWhiteSpace(BaseUrl) || string.IsNullOrWhiteSpace(ApiKey))
        {
            Status = "缺少接口地址或密钥";
            return;
        }

        var role = BackendProfileRole.Normalize(SelectedProfileRole);
        SelectedProfileRole = role;
        SelectedProtocol = BackendProtocol.Normalize(SelectedProtocol);
        var compatibilityError = ValidateRoleProtocol(role, SelectedProtocol);
        if (!string.IsNullOrWhiteSpace(compatibilityError))
        {
            Status = compatibilityError;
            return;
        }

        BaseUrl = BackendProtocol.NormalizeBaseUrl(BaseUrl, SelectedProtocol);
        _profiles.Upsert(new BackendProfile
        {
            Id = BackendProfileRole.DefaultProfileId(role),
            Name = string.IsNullOrWhiteSpace(Name) ? BackendProfileRole.DisplayName(role) : Name.Trim(),
            BaseUrl = BaseUrl,
            ApiKey = ApiKey,
            Protocol = SelectedProtocol,
            MainlineModel = MainlineModel,
            ImageModel = ImageModel,
            VideoModel = VideoModel,
            Concurrency = Math.Max(1, Concurrency),
            Priority = 0,
            IsEnabled = true,
            SupportsPromptOptimization = role == BackendProfileRole.Prompt,
            SupportsChat = role is BackendProfileRole.Prompt or BackendProfileRole.Chat,
            SupportsImageGeneration = role == BackendProfileRole.Image,
            SupportsVideoGeneration = role == BackendProfileRole.Video,
            SupportsAgent = role == BackendProfileRole.Agent
        });
        Status = $"已保存 {BackendProfileRole.DisplayName(role)}";
    }

    private void LoadProfileForRole(string role)
    {
        role = BackendProfileRole.Normalize(role);
        var profile = _profiles.GetById(BackendProfileRole.DefaultProfileId(role))
                      ?? LoadLegacyProfile(role);
        if (profile is null)
        {
            LoadDefaultsForRole(role);
            return;
        }

        try
        {
            _isLoadingProfile = true;
            SelectedProfileRole = role;
            Name = profile.Name;
            BaseUrl = profile.BaseUrl;
            ApiKey = profile.ApiKey;
            SelectedProtocol = BackendProtocol.Normalize(profile.Protocol);
            MainlineModel = NormalizeMainlineModel(profile.MainlineModel);
            ImageModel = string.IsNullOrWhiteSpace(profile.ImageModel) ? "gpt-image-2" : profile.ImageModel;
            VideoModel = string.IsNullOrWhiteSpace(profile.VideoModel) ? DefaultRoutinVideoModel : profile.VideoModel;
            Concurrency = Math.Max(1, profile.Concurrency);
        }
        finally
        {
            _isLoadingProfile = false;
            OnPropertyChanged(nameof(SelectedProfileRoleDescription));
            OnPropertyChanged(nameof(SelectedProtocolDescription));
        }
    }

    private BackendProfile? LoadLegacyProfile(string role)
    {
        return role switch
        {
            BackendProfileRole.Video => _profiles.GetById(LegacyRoutinVideoProfileId),
            BackendProfileRole.Image or BackendProfileRole.Prompt or BackendProfileRole.Chat => _profiles.GetById(LegacyDefaultProfileId),
            _ => null
        };
    }

    private void LoadDefaultsForRole(string role)
    {
        try
        {
            _isLoadingProfile = true;
            SelectedProfileRole = role;
            switch (role)
            {
                case BackendProfileRole.Video:
                    Name = "视频生成 API";
                    BaseUrl = DefaultRoutinBaseUrl;
                    ApiKey = "";
                    SelectedProtocol = BackendProtocol.RoutinXaiVideo;
                    MainlineModel = DefaultMainlineModel;
                    ImageModel = "gpt-image-2";
                    VideoModel = DefaultRoutinVideoModel;
                    Concurrency = 1;
                    break;
                case BackendProfileRole.Agent:
                    Name = "Agent / Responses API";
                    BaseUrl = "https://api.openai.com/v1";
                    ApiKey = "";
                    SelectedProtocol = BackendProtocol.OpenAiResponses;
                    MainlineModel = DefaultMainlineModel;
                    ImageModel = "gpt-image-2";
                    VideoModel = DefaultRoutinVideoModel;
                    Concurrency = 1;
                    break;
                case BackendProfileRole.Prompt:
                    Name = "提示词润色 / 对话 API";
                    BaseUrl = "https://api.openai.com/v1";
                    ApiKey = "";
                    SelectedProtocol = BackendProtocol.ChatCompletionsImageJson;
                    MainlineModel = DefaultMainlineModel;
                    ImageModel = "gpt-image-2";
                    VideoModel = DefaultRoutinVideoModel;
                    Concurrency = 1;
                    break;
                case BackendProfileRole.Chat:
                    Name = "聊天 API";
                    BaseUrl = "https://api.openai.com/v1";
                    ApiKey = "";
                    SelectedProtocol = BackendProtocol.ChatCompletionsImageJson;
                    MainlineModel = DefaultMainlineModel;
                    ImageModel = "gpt-image-2";
                    VideoModel = DefaultRoutinVideoModel;
                    Concurrency = 1;
                    break;
                default:
                    Name = "图片生成 API";
                    BaseUrl = "https://api.openai.com/v1";
                    ApiKey = "";
                    SelectedProtocol = BackendProtocol.OpenAiImages;
                    MainlineModel = DefaultMainlineModel;
                    ImageModel = "gpt-image-2";
                    VideoModel = DefaultRoutinVideoModel;
                    Concurrency = 1;
                    break;
            }
        }
        finally
        {
            _isLoadingProfile = false;
            OnPropertyChanged(nameof(SelectedProfileRoleDescription));
            OnPropertyChanged(nameof(SelectedProtocolDescription));
        }
    }

    private static string ValidateRoleProtocol(string role, string protocol)
    {
        return role switch
        {
            BackendProfileRole.Video when !BackendProtocol.SupportsVideo(protocol) => "视频生成 API 需要选择视频协议。",
            BackendProfileRole.Image when !BackendProtocol.SupportsImage(protocol) => "图片生成 API 需要选择图片生成协议。",
            BackendProfileRole.Agent when !BackendProtocol.SupportsAgent(protocol) => "Agent / Responses API 需要选择 OpenAI Responses 协议。",
            BackendProfileRole.Prompt or BackendProfileRole.Chat when !BackendProtocol.SupportsChat(protocol) => "提示词润色/聊天 API 需要选择支持 /v1/chat/completions 的协议。",
            _ => ""
        };
    }

    private static string NormalizeMainlineModel(string value)
    {
        var trimmed = value.Trim();
        return string.IsNullOrWhiteSpace(trimmed)
               || string.Equals(trimmed, "auto", StringComparison.OrdinalIgnoreCase)
               || string.Equals(trimmed, "gpt-5.5", StringComparison.OrdinalIgnoreCase)
            ? DefaultMainlineModel
            : trimmed;
    }

    partial void OnSelectedProfileRoleChanged(string value)
    {
        OnPropertyChanged(nameof(SelectedProfileRoleDescription));
        if (_isLoadingProfile)
        {
            return;
        }

        LoadProfileForRole(value);
    }

    partial void OnSelectedProtocolChanged(string value)
    {
        OnPropertyChanged(nameof(SelectedProtocolDescription));
        if (_isLoadingProfile)
        {
            return;
        }

        var protocol = BackendProtocol.Normalize(value);
        SelectedProtocol = protocol;
        if (protocol == BackendProtocol.RoutinXaiVideo)
        {
            SelectedProfileRole = BackendProfileRole.Video;
            if (string.IsNullOrWhiteSpace(BaseUrl) || BaseUrl.Contains("api.openai.com", StringComparison.OrdinalIgnoreCase))
            {
                BaseUrl = DefaultRoutinBaseUrl;
            }

            if (string.IsNullOrWhiteSpace(VideoModel) || string.Equals(VideoModel, "gpt-image-2", StringComparison.OrdinalIgnoreCase))
            {
                VideoModel = DefaultRoutinVideoModel;
            }
        }
    }

    private static string DescriptionForRole(string role) => BackendProfileRole.Normalize(role) switch
    {
        BackendProfileRole.Prompt => "用于提示词润色；可以填和图片 API 相同的网关，也可以单独填支持 /v1/chat/completions 的对话 API。",
        BackendProfileRole.Chat => "用于聊天页面；和提示词润色可共用同一个平台，也可单独配置。",
        BackendProfileRole.Video => "只用于视频生成；不会影响图片生成和提示词润色。",
        BackendProfileRole.Agent => "用于 Agent/Responses 流式生图工作流。",
        _ => "只用于图片生成；不会被视频生成抢占。"
    };
}

public sealed class ProfileRoleOptionViewModel
{
    public ProfileRoleOptionViewModel(string value, string label, string description)
    {
        Value = value;
        Label = label;
        Description = description;
    }

    public string Value { get; }
    public string Label { get; }
    public string Description { get; }
}

public sealed class ProtocolOptionViewModel
{
    public ProtocolOptionViewModel(string value, string label, string description)
    {
        Value = value;
        Label = label;
        Description = description;
    }

    public string Value { get; }
    public string Label { get; }
    public string Description { get; }
}
