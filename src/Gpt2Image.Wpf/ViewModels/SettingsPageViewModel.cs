using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gpt2Image.Core.Models;
using Gpt2Image.Core.Storage.Repositories;

namespace Gpt2Image.Wpf.ViewModels;

public sealed partial class SettingsPageViewModel : ObservableObject
{
    private const string DefaultProfileId = "default";
    private readonly BackendProfileRepository _profiles;

    [ObservableProperty]
    private string _name = "OpenAI";

    [ObservableProperty]
    private string _baseUrl = "https://api.openai.com/v1";

    [ObservableProperty]
    private string _apiKey = "";

    [ObservableProperty]
    private string _selectedProtocol = BackendProtocol.OpenAiImages;

    [ObservableProperty]
    private string _mainlineModel = "gpt-5.5";

    [ObservableProperty]
    private string _imageModel = "gpt-image-2";

    [ObservableProperty]
    private int _concurrency = 1;

    [ObservableProperty]
    private string _status = "";

    public SettingsPageViewModel(BackendProfileRepository profiles)
    {
        _profiles = profiles;
        LoadDefaultProfile();
    }

    public IReadOnlyList<ProtocolOptionViewModel> ProtocolOptions { get; } = BackendProtocol.KnownValues
        .Select(value => new ProtocolOptionViewModel(value, BackendProtocol.DisplayName(value), BackendProtocol.Description(value)))
        .ToList();

    public string SelectedProtocolDescription => BackendProtocol.Description(SelectedProtocol);

    [RelayCommand]
    private void Save()
    {
        if (string.IsNullOrWhiteSpace(BaseUrl) || string.IsNullOrWhiteSpace(ApiKey))
        {
            Status = "缺少接口地址或密钥";
            return;
        }

        SelectedProtocol = BackendProtocol.Normalize(SelectedProtocol);
        BaseUrl = BackendProtocol.NormalizeBaseUrl(BaseUrl, SelectedProtocol);
        _profiles.Upsert(new BackendProfile
        {
            Id = DefaultProfileId,
            Name = Name,
            BaseUrl = BaseUrl,
            ApiKey = ApiKey,
            Protocol = SelectedProtocol,
            MainlineModel = MainlineModel,
            ImageModel = ImageModel,
            Concurrency = Math.Max(1, Concurrency),
            Priority = 0,
            IsEnabled = true
        });
        Status = "已保存";
    }

    private void LoadDefaultProfile()
    {
        var profile = _profiles.GetById(DefaultProfileId);
        if (profile is null)
        {
            return;
        }

        Name = profile.Name;
        BaseUrl = profile.BaseUrl;
        ApiKey = profile.ApiKey;
        SelectedProtocol = BackendProtocol.Normalize(profile.Protocol);
        MainlineModel = profile.MainlineModel;
        ImageModel = profile.ImageModel;
        Concurrency = Math.Max(1, profile.Concurrency);
    }

    partial void OnSelectedProtocolChanged(string value)
    {
        OnPropertyChanged(nameof(SelectedProtocolDescription));
    }
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
