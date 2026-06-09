using System.Collections.ObjectModel;
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
    private string _editingProfileId = "";

    [ObservableProperty]
    private string _selectedProfileRole = BackendProfileRole.Image;

    [ObservableProperty]
    private BackendProfileItemViewModel? _selectedProfile;

    [ObservableProperty]
    private string _selectedProviderKind = BackendProviderKind.Custom;

    [ObservableProperty]
    private string _name = "图像创作 API";

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

    public ObservableCollection<BackendProfileItemViewModel> Profiles { get; } = new();

    public IReadOnlyList<ProfileRoleOptionViewModel> RoleOptions { get; } = BackendProfileRole.KnownValues
        .Select(value => new ProfileRoleOptionViewModel(value, BackendProfileRole.DisplayName(value), DescriptionForRole(value)))
        .ToList();

    public ObservableCollection<ProtocolOptionViewModel> ProtocolOptions { get; } = new();

    public ObservableCollection<ProviderPresetOptionViewModel> ProviderOptions { get; } = new();

    public string SelectedProfileRoleDescription => RoleOptions.FirstOrDefault(item => item.Value == BackendProfileRole.Normalize(SelectedProfileRole))?.Description ?? "";

    public string SelectedProtocolDescription => BackendProtocol.Description(SelectedProtocol);

    public string SelectedProviderDescription => ProviderOptions.FirstOrDefault(item => item.Value == BackendProviderKind.Normalize(SelectedProviderKind))?.Description ?? "";

    public string ProviderSectionTitle => BackendProfileRole.Normalize(SelectedProfileRole) switch
    {
        BackendProfileRole.Image => "图像生成供应商（仅显示图片能力）",
        BackendProfileRole.Video => "视频生成供应商（仅显示视频能力）",
        BackendProfileRole.Agent => "Agent 供应商（Responses 能力）",
        BackendProfileRole.Prompt or BackendProfileRole.Chat or BackendProfileRole.Coding => "对话 / 编码供应商（仅显示文本能力）",
        _ => "模型接口供应商"
    };

    public string ProviderSectionHint => BackendProfileRole.Normalize(SelectedProfileRole) switch
    {
        BackendProfileRole.Image => "DeepSeek、Kimi、Qwen、GLM 等普通聊天模型不会出现在图像生成里；这里只显示 OpenAI Images/Responses 或自定义图片兼容接口。",
        BackendProfileRole.Video => "DeepSeek、MiniMax、Kimi、Qwen、GLM 等普通聊天模型不会出现在视频生成里；这里只显示 Routin xAI Video 等视频接口。",
        BackendProfileRole.Agent => "这里只显示支持 /v1/responses 的 Agent/工具调用接口。",
        BackendProfileRole.Prompt or BackendProfileRole.Chat or BackendProfileRole.Coding => "这里只显示 OpenAI-compatible 文本/代码模型：DeepSeek、MiniMax、MiMo、Kimi、Qwen、GLM 等。",
        _ => "按当前功能过滤可用供应商，避免把聊天模型误用于图片或视频生成。"
    };

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
        SelectedProviderKind = BackendProviderKind.Normalize(SelectedProviderKind);
        SelectedProtocol = BackendProtocol.Normalize(SelectedProtocol);
        var compatibilityError = ValidateRoleProvider(role, SelectedProviderKind);
        if (string.IsNullOrWhiteSpace(compatibilityError))
        {
            compatibilityError = ValidateRoleProtocol(role, SelectedProtocol);
        }

        if (!string.IsNullOrWhiteSpace(compatibilityError))
        {
            Status = compatibilityError;
            return;
        }

        BaseUrl = BackendProtocol.NormalizeBaseUrl(BaseUrl, SelectedProtocol);
        var id = string.IsNullOrWhiteSpace(_editingProfileId)
            ? BuildProfileId(role, SelectedProviderKind)
            : _editingProfileId;
        var normalizedName = string.IsNullOrWhiteSpace(Name) ? BackendProfileRole.DisplayName(role) : Name.Trim();
        _profiles.Upsert(new BackendProfile
        {
            Id = id,
            Name = normalizedName,
            BaseUrl = BaseUrl,
            ApiKey = ApiKey,
            Protocol = SelectedProtocol,
            ProviderKind = SelectedProviderKind,
            MainlineModel = MainlineModel.Trim(),
            ImageModel = ImageModel.Trim(),
            VideoModel = VideoModel.Trim(),
            Concurrency = Math.Max(1, Concurrency),
            Priority = ComputePriority(role, SelectedProviderKind),
            IsEnabled = true,
            SupportsPromptOptimization = role == BackendProfileRole.Prompt,
            SupportsChat = role is BackendProfileRole.Prompt or BackendProfileRole.Chat or BackendProfileRole.Coding,
            SupportsImageGeneration = role == BackendProfileRole.Image,
            SupportsVideoGeneration = role == BackendProfileRole.Video,
            SupportsAgent = role == BackendProfileRole.Agent
        });
        _editingProfileId = id;
        RefreshProfilesForCurrentRole(id);
        Status = $"已保存 {normalizedName}";
    }

    [RelayCommand]
    private void NewProfile()
    {
        var role = BackendProfileRole.Normalize(SelectedProfileRole);
        LoadDefaultsForRole(role, resetApiKey: true);
        _editingProfileId = BuildProfileId(role, SelectedProviderKind, forceUnique: true);
        SelectedProfile = null;
        Status = "已创建新配置草稿，选择供应商后保存。";
    }

    [RelayCommand]
    private void ApplyProviderPreset()
    {
        ApplySelectedProviderPreset(clearApiKeyWhenSwitchingProvider: false);
        Status = $"已应用 {BackendProviderKind.DisplayName(SelectedProviderKind)} 预设，可继续填写密钥并保存。";
    }

    [RelayCommand]
    private void RefreshProfiles()
    {
        RefreshProfilesForCurrentRole(_editingProfileId);
        Status = $"已刷新 {BackendProfileRole.DisplayName(SelectedProfileRole)} 配置列表。";
    }

    private void LoadProfileForRole(string role)
    {
        role = BackendProfileRole.Normalize(role);
        RefreshOptionsForRole(role);
        RefreshProfilesForRole(role);
        var profile = _profiles.GetById(BackendProfileRole.DefaultProfileId(role))
                      ?? LoadLegacyProfile(role)
                      ?? _profiles.ListForRole(role, includeDisabled: true).FirstOrDefault();
        if (profile is null)
        {
            LoadDefaultsForRole(role, resetApiKey: true);
            _editingProfileId = BackendProfileRole.DefaultProfileId(role);
            return;
        }

        LoadProfile(profile, role);
    }

    private void LoadProfile(BackendProfile profile, string? roleOverride = null)
    {
        try
        {
            _isLoadingProfile = true;
            _editingProfileId = profile.Id;
            var role = string.IsNullOrWhiteSpace(roleOverride) ? GuessPrimaryRole(profile) : BackendProfileRole.Normalize(roleOverride);
            SelectedProfileRole = role;
            RefreshOptionsForRole(role);
            SelectedProfile = Profiles.FirstOrDefault(item => item.Id == profile.Id);
            SelectedProviderKind = BackendProviderKind.Normalize(profile.ProviderKind);
            Name = profile.Name;
            BaseUrl = profile.BaseUrl;
            ApiKey = profile.ApiKey;
            SelectedProtocol = BackendProtocol.Normalize(profile.Protocol);
            MainlineModel = NormalizeMainlineModel(profile.MainlineModel, SelectedProviderKind);
            ImageModel = string.IsNullOrWhiteSpace(profile.ImageModel) ? "gpt-image-2" : profile.ImageModel;
            VideoModel = string.IsNullOrWhiteSpace(profile.VideoModel) ? DefaultRoutinVideoModel : profile.VideoModel;
            Concurrency = Math.Max(1, profile.Concurrency);
        }
        finally
        {
            _isLoadingProfile = false;
            RaiseSelectionDescriptions();
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

    private void RefreshOptionsForRole(string role)
    {
        var normalizedRole = BackendProfileRole.Normalize(role);
        var wasLoading = _isLoadingProfile;
        try
        {
            _isLoadingProfile = true;
            ProtocolOptions.Clear();
            foreach (var protocol in ProtocolsForRole(normalizedRole))
            {
                ProtocolOptions.Add(new ProtocolOptionViewModel(protocol, BackendProtocol.DisplayName(protocol), BackendProtocol.Description(protocol)));
            }

            ProviderOptions.Clear();
            foreach (var provider in ProvidersForRole(normalizedRole))
            {
                ProviderOptions.Add(provider);
            }

            if (!ProviderOptions.Any(item => item.Value == BackendProviderKind.Normalize(SelectedProviderKind)))
            {
                SelectedProviderKind = ProviderOptions.FirstOrDefault()?.Value ?? BackendProviderKind.Custom;
            }

            if (!ProtocolOptions.Any(item => item.Value == BackendProtocol.Normalize(SelectedProtocol)))
            {
                SelectedProtocol = ProtocolOptions.FirstOrDefault()?.Value ?? BackendProtocol.OpenAiImages;
            }
        }
        finally
        {
            _isLoadingProfile = wasLoading;
        }

        RaiseSelectionDescriptions();
    }

    private static IReadOnlyList<string> ProtocolsForRole(string role) => BackendProfileRole.Normalize(role) switch
    {
        BackendProfileRole.Video => new[] { BackendProtocol.RoutinXaiVideo },
        BackendProfileRole.Agent => new[] { BackendProtocol.OpenAiResponses },
        BackendProfileRole.Prompt or BackendProfileRole.Chat or BackendProfileRole.Coding => new[] { BackendProtocol.ChatCompletionsImageJson },
        _ => new[] { BackendProtocol.OpenAiImages, BackendProtocol.OpenAiResponses, BackendProtocol.ChatCompletionsImageJson }
    };

    private static IReadOnlyList<ProviderPresetOptionViewModel> ProvidersForRole(string role)
    {
        return BackendProfileRole.Normalize(role) switch
        {
            BackendProfileRole.Video => new[]
            {
                new ProviderPresetOptionViewModel(BackendProviderKind.Routin, "Routin xAI Video", "视频生成专用接口；DeepSeek/Kimi/Qwen/GLM 这类普通聊天模型不会用于视频生成。")
            },
            BackendProfileRole.Image => new[]
            {
                new ProviderPresetOptionViewModel(BackendProviderKind.OpenAi, "OpenAI 图像", "图像生成/编辑能力：OpenAI Images 或 Responses image_generation。"),
                new ProviderPresetOptionViewModel(BackendProviderKind.Custom, "自定义图片兼容接口", "仅用于明确支持图片生成/编辑的 OpenAI-compatible 图片接口；不要填写纯聊天模型。")
            },
            BackendProfileRole.Agent => new[]
            {
                new ProviderPresetOptionViewModel(BackendProviderKind.OpenAi, "OpenAI Responses", "支持 /v1/responses 和 image_generation 工具的接口。"),
                new ProviderPresetOptionViewModel(BackendProviderKind.Custom, "自定义 Responses 接口", "仅用于明确支持 /v1/responses 的兼容接口。")
            },
            BackendProfileRole.Prompt or BackendProfileRole.Chat or BackendProfileRole.Coding => BackendProviderPresetCatalog.TextCompatiblePresets
                .Select(item => new ProviderPresetOptionViewModel(item.Kind, item.Label, item.Description))
                .ToList(),
            _ => new[] { new ProviderPresetOptionViewModel(BackendProviderKind.Custom, "自定义", "自定义兼容接口。") }
        };
    }

    private void LoadDefaultsForRole(string role, bool resetApiKey)
    {
        role = BackendProfileRole.Normalize(role);
        RefreshOptionsForRole(role);
        try
        {
            _isLoadingProfile = true;
            SelectedProfileRole = role;
            SelectedProviderKind = role == BackendProfileRole.Video ? BackendProviderKind.Routin : BackendProviderKind.OpenAi;
            ApiKey = resetApiKey ? "" : ApiKey;
            switch (role)
            {
                case BackendProfileRole.Video:
                    Name = "AI 视频创作 API";
                    BaseUrl = DefaultRoutinBaseUrl;
                    SelectedProtocol = BackendProtocol.RoutinXaiVideo;
                    MainlineModel = DefaultMainlineModel;
                    ImageModel = "gpt-image-2";
                    VideoModel = DefaultRoutinVideoModel;
                    Concurrency = 1;
                    break;
                case BackendProfileRole.Agent:
                    Name = "Agent / Responses API";
                    BaseUrl = "https://api.openai.com/v1";
                    SelectedProtocol = BackendProtocol.OpenAiResponses;
                    MainlineModel = DefaultMainlineModel;
                    ImageModel = "gpt-image-2";
                    VideoModel = DefaultRoutinVideoModel;
                    Concurrency = 1;
                    break;
                case BackendProfileRole.Prompt:
                    Name = "提示词润色 API";
                    BaseUrl = "https://api.openai.com/v1";
                    SelectedProtocol = BackendProtocol.ChatCompletionsImageJson;
                    MainlineModel = DefaultMainlineModel;
                    ImageModel = "gpt-image-2";
                    VideoModel = DefaultRoutinVideoModel;
                    Concurrency = 1;
                    break;
                case BackendProfileRole.Chat:
                    Name = "AI 对话 API";
                    BaseUrl = "https://api.openai.com/v1";
                    SelectedProtocol = BackendProtocol.ChatCompletionsImageJson;
                    MainlineModel = DefaultMainlineModel;
                    ImageModel = "gpt-image-2";
                    VideoModel = DefaultRoutinVideoModel;
                    Concurrency = 1;
                    break;
                case BackendProfileRole.Coding:
                    Name = "AI 编码 API";
                    BaseUrl = "https://api.openai.com/v1";
                    SelectedProtocol = BackendProtocol.ChatCompletionsImageJson;
                    MainlineModel = DefaultMainlineModel;
                    ImageModel = "gpt-image-2";
                    VideoModel = DefaultRoutinVideoModel;
                    Concurrency = 1;
                    break;
                default:
                    Name = "图像创作 API";
                    BaseUrl = "https://api.openai.com/v1";
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
            RaiseSelectionDescriptions();
        }
    }

    private void ApplySelectedProviderPreset(bool clearApiKeyWhenSwitchingProvider)
    {
        var role = BackendProfileRole.Normalize(SelectedProfileRole);
        var provider = EnsureProviderAllowedForRole(role, BackendProviderKind.Normalize(SelectedProviderKind));
        SelectedProviderKind = provider;

        if (role == BackendProfileRole.Video)
        {
            Name = "Routin xAI Video";
            BaseUrl = DefaultRoutinBaseUrl;
            SelectedProtocol = BackendProtocol.RoutinXaiVideo;
            MainlineModel = DefaultMainlineModel;
            ImageModel = "gpt-image-2";
            VideoModel = DefaultRoutinVideoModel;
            if (clearApiKeyWhenSwitchingProvider)
            {
                ApiKey = "";
            }
            return;
        }

        if (role == BackendProfileRole.Image)
        {
            Name = provider == BackendProviderKind.OpenAi ? "OpenAI · 图像创作 API" : "自定义图片兼容接口";
            BaseUrl = provider == BackendProviderKind.OpenAi ? "https://api.openai.com/v1" : "https://api.example.com/v1";
            SelectedProtocol = provider == BackendProviderKind.OpenAi ? BackendProtocol.OpenAiImages : BackendProtocol.OpenAiImages;
            MainlineModel = DefaultMainlineModel;
            ImageModel = string.IsNullOrWhiteSpace(ImageModel) ? "gpt-image-2" : ImageModel;
            VideoModel = DefaultRoutinVideoModel;
            if (clearApiKeyWhenSwitchingProvider)
            {
                ApiKey = "";
            }
            return;
        }

        if (role == BackendProfileRole.Agent)
        {
            Name = provider == BackendProviderKind.OpenAi ? "OpenAI · Agent / Responses API" : "自定义 Responses 接口";
            BaseUrl = provider == BackendProviderKind.OpenAi ? "https://api.openai.com/v1" : "https://api.example.com/v1";
            SelectedProtocol = BackendProtocol.OpenAiResponses;
            MainlineModel = provider == BackendProviderKind.OpenAi ? DefaultMainlineModel : MainlineModel;
            if (clearApiKeyWhenSwitchingProvider)
            {
                ApiKey = "";
            }
            return;
        }

        var preset = BackendProviderPresetCatalog.GetTextPreset(provider);
        Name = provider == BackendProviderKind.Custom
            ? $"自定义 · {BackendProfileRole.DisplayName(role)}"
            : $"{preset.Label} · {BackendProfileRole.DisplayName(role)}";
        BaseUrl = preset.BaseUrl;
        SelectedProtocol = BackendProtocol.ChatCompletionsImageJson;
        if (!string.IsNullOrWhiteSpace(preset.MainlineModel))
        {
            MainlineModel = preset.MainlineModel;
        }

        ImageModel = "";
        VideoModel = "";
        if (clearApiKeyWhenSwitchingProvider)
        {
            ApiKey = "";
        }
    }

    private string EnsureProviderAllowedForRole(string role, string provider)
    {
        var normalized = BackendProviderKind.Normalize(provider);
        if (ProviderOptions.Any(item => item.Value == normalized))
        {
            return normalized;
        }

        RefreshOptionsForRole(role);
        return ProviderOptions.FirstOrDefault()?.Value ?? BackendProviderKind.Custom;
    }

    private void RefreshProfilesForCurrentRole(string? preferredId = null)
    {
        RefreshProfilesForRole(SelectedProfileRole);
        SelectedProfile = string.IsNullOrWhiteSpace(preferredId)
            ? Profiles.FirstOrDefault()
            : Profiles.FirstOrDefault(item => item.Id == preferredId) ?? Profiles.FirstOrDefault();
    }

    private void RefreshProfilesForRole(string role)
    {
        Profiles.Clear();
        foreach (var profile in _profiles.ListForRole(role, includeDisabled: true))
        {
            Profiles.Add(BackendProfileItemViewModel.FromProfile(profile));
        }
    }

    private static string ValidateRoleProvider(string role, string providerKind)
    {
        var normalizedRole = BackendProfileRole.Normalize(role);
        var normalizedProvider = BackendProviderKind.Normalize(providerKind);
        return normalizedRole switch
        {
            BackendProfileRole.Video when normalizedProvider != BackendProviderKind.Routin => "视频生成只能选择视频供应商，不能使用 DeepSeek/Kimi/Qwen/GLM 等纯文本模型。",
            BackendProfileRole.Image when normalizedProvider is BackendProviderKind.DeepSeek or BackendProviderKind.MiniMax or BackendProviderKind.Mimo or BackendProviderKind.Mino or BackendProviderKind.Kimi or BackendProviderKind.Qwen or BackendProviderKind.Glm => "图像创作不能选择纯文本/编码模型；请选择 OpenAI 图像或自定义图片兼容接口。",
            BackendProfileRole.Agent when normalizedProvider is BackendProviderKind.DeepSeek or BackendProviderKind.MiniMax or BackendProviderKind.Mimo or BackendProviderKind.Mino or BackendProviderKind.Kimi or BackendProviderKind.Qwen or BackendProviderKind.Glm or BackendProviderKind.Routin => "Agent/Responses 只能选择支持 /v1/responses 的供应商。",
            BackendProfileRole.Prompt or BackendProfileRole.Chat or BackendProfileRole.Coding when normalizedProvider == BackendProviderKind.Routin => "对话/编码不能选择视频生成供应商。",
            _ => ""
        };
    }

    private static string ValidateRoleProtocol(string role, string protocol)
    {
        return role switch
        {
            BackendProfileRole.Video when !BackendProtocol.SupportsVideo(protocol) => "视频生成 API 需要选择视频协议。",
            BackendProfileRole.Image when !BackendProtocol.SupportsImage(protocol) => "图像创作 API 需要选择图片生成协议。",
            BackendProfileRole.Agent when !BackendProtocol.SupportsAgent(protocol) => "Agent / Responses API 需要选择 OpenAI Responses 协议。",
            BackendProfileRole.Prompt or BackendProfileRole.Chat or BackendProfileRole.Coding when !BackendProtocol.SupportsChat(protocol) => "提示词润色/聊天/AI 编码 API 需要选择支持 /v1/chat/completions 的协议。",
            _ => ""
        };
    }

    private static string NormalizeMainlineModel(string value, string providerKind)
    {
        var trimmed = value.Trim();
        if (!string.IsNullOrWhiteSpace(trimmed)
            && !string.Equals(trimmed, "auto", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(trimmed, "gpt-5.5", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        var preset = BackendProviderPresetCatalog.GetTextPreset(providerKind);
        return string.IsNullOrWhiteSpace(preset.MainlineModel) ? DefaultMainlineModel : preset.MainlineModel;
    }

    private static string BuildProfileId(string role, string providerKind, bool forceUnique = false)
    {
        var normalizedRole = BackendProfileRole.Normalize(role);
        var normalizedProvider = BackendProviderKind.Normalize(providerKind);
        if (!forceUnique && normalizedProvider == BackendProviderKind.OpenAi)
        {
            return BackendProfileRole.DefaultProfileId(normalizedRole);
        }

        var suffix = forceUnique ? $"-{Guid.NewGuid():N}"[..9] : "";
        return $"{normalizedRole}-{normalizedProvider}{suffix}";
    }

    private static int ComputePriority(string role, string providerKind)
    {
        if (BackendProviderKind.Normalize(providerKind) == BackendProviderKind.OpenAi)
        {
            return 0;
        }

        return BackendProfileRole.Normalize(role) is BackendProfileRole.Chat or BackendProfileRole.Coding ? 10 : 0;
    }

    private static string GuessPrimaryRole(BackendProfile profile)
    {
        if (profile.SupportsVideoGeneration)
        {
            return BackendProfileRole.Video;
        }

        if (profile.SupportsAgent)
        {
            return BackendProfileRole.Agent;
        }

        if (profile.SupportsImageGeneration)
        {
            return BackendProfileRole.Image;
        }

        if (profile.SupportsPromptOptimization)
        {
            return BackendProfileRole.Prompt;
        }

        if (profile.SupportsChat)
        {
            return BackendProfileRole.Chat;
        }

        return BackendProfileRole.Image;
    }

    private void RaiseSelectionDescriptions()
    {
        OnPropertyChanged(nameof(SelectedProfileRoleDescription));
        OnPropertyChanged(nameof(SelectedProtocolDescription));
        OnPropertyChanged(nameof(SelectedProviderDescription));
        OnPropertyChanged(nameof(ProviderSectionTitle));
        OnPropertyChanged(nameof(ProviderSectionHint));
    }

    partial void OnSelectedProfileChanged(BackendProfileItemViewModel? value)
    {
        if (_isLoadingProfile || value is null)
        {
            return;
        }

        var profile = _profiles.GetById(value.Id);
        if (profile is not null)
        {
            LoadProfile(profile, SelectedProfileRole);
        }
    }

    partial void OnSelectedProfileRoleChanged(string value)
    {
        OnPropertyChanged(nameof(SelectedProfileRoleDescription));
        if (_isLoadingProfile)
        {
            return;
        }

        LoadProfileForRole(value);
        if (Profiles.Count == 0 && BackendProfileRole.Normalize(value) != BackendProfileRole.Video)
        {
            ApiKey = "";
        }
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
            SelectedProviderKind = BackendProviderKind.Routin;
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

    partial void OnSelectedProviderKindChanged(string value)
    {
        OnPropertyChanged(nameof(SelectedProviderDescription));
        if (_isLoadingProfile)
        {
            return;
        }

        SelectedProviderKind = BackendProviderKind.Normalize(value);
        ApplySelectedProviderPreset(clearApiKeyWhenSwitchingProvider: true);
        _editingProfileId = BuildProfileId(SelectedProfileRole, SelectedProviderKind);
    }

    private static string DescriptionForRole(string role) => BackendProfileRole.Normalize(role) switch
    {
        BackendProfileRole.Prompt => "用于提示词润色；可选择 OpenAI、DeepSeek、MiniMax、Kimi、Qwen、GLM 等聊天兼容模型。",
        BackendProfileRole.Chat => "用于日常 AI 对话；支持为每个会话选择不同供应商配置。",
        BackendProfileRole.Video => "只用于视频生成；不会影响图像创作和提示词润色。",
        BackendProfileRole.Agent => "用于 Agent/Responses 流式生图工作流。",
        BackendProfileRole.Coding => "用于 AI 编码；支持 DeepSeek、Kimi、Qwen、GLM 等聊天兼容模型生成计划、文件变更提案和验证命令。",
        _ => "只用于图像创作；不会被视频生成和对话配置抢占。"
    };
}

public sealed class BackendProfileItemViewModel
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Provider { get; init; } = "";
    public string Model { get; init; } = "";
    public string Endpoint { get; init; } = "";
    public bool IsEnabled { get; init; }
    public string DisplayText => $"{Name} · {Provider}";
    public string DetailText => $"{Model} · {Endpoint}";

    public static BackendProfileItemViewModel FromProfile(BackendProfile profile) => new()
    {
        Id = profile.Id,
        Name = profile.Name,
        Provider = BackendProviderKind.DisplayName(profile.ProviderKind),
        Model = string.IsNullOrWhiteSpace(profile.MainlineModel) ? profile.ImageModel : profile.MainlineModel,
        Endpoint = profile.BaseUrl,
        IsEnabled = profile.IsEnabled
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

public sealed class ProviderPresetOptionViewModel
{
    public ProviderPresetOptionViewModel(string value, string label, string description)
    {
        Value = value;
        Label = label;
        Description = description;
    }

    public string Value { get; }
    public string Label { get; }
    public string Description { get; }
}
