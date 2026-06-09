using Gpt2Image.Core.Models;
using Gpt2Image.Core.Security;
using Gpt2Image.Core.Storage;
using Gpt2Image.Core.Storage.Repositories;
using Gpt2Image.Wpf.ViewModels;
using Microsoft.Data.Sqlite;

namespace Gpt2Image.Tests.Wpf;

public sealed class SettingsPageViewModelTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "gpt2image-settings-vm-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Constructor_loads_saved_default_profile_including_api_key()
    {
        var repository = CreateRepository();
        repository.Upsert(new BackendProfile
        {
            Id = "default",
            Name = "Custom",
            BaseUrl = "https://example.test/v1",
            ApiKey = "sk-saved",
            Protocol = BackendProtocol.OpenAiResponses,
            MainlineModel = "main-model",
            ImageModel = "image-model",
            VideoModel = "video-model",
            Concurrency = 3,
            Priority = 0,
            IsEnabled = true
        });

        var viewModel = new SettingsPageViewModel(repository);

        Assert.Equal("Custom", viewModel.Name);
        Assert.Equal("https://example.test/v1", viewModel.BaseUrl);
        Assert.Equal("sk-saved", viewModel.ApiKey);
        Assert.Equal(BackendProtocol.OpenAiResponses, viewModel.SelectedProtocol);
        Assert.Equal("main-model", viewModel.MainlineModel);
        Assert.Equal("image-model", viewModel.ImageModel);
        Assert.Equal("video-model", viewModel.VideoModel);
        Assert.Equal(3, viewModel.Concurrency);
    }

    [Fact]
    public void Save_persists_api_key_for_next_settings_page_instance()
    {
        var repository = CreateRepository();
        var viewModel = new SettingsPageViewModel(repository)
        {
            Name = "OpenAI",
            BaseUrl = "https://api.openai.com",
            ApiKey = "sk-new",
            SelectedProtocol = BackendProtocol.ChatCompletionsImageJson,
            MainlineModel = "gpt-5.5",
            ImageModel = "gpt-image-2",
            Concurrency = 2
        };

        viewModel.SaveCommand.Execute(null);

        var reloaded = new SettingsPageViewModel(repository);
        Assert.Equal("sk-new", reloaded.ApiKey);
        Assert.Equal("https://api.openai.com/v1", reloaded.BaseUrl);
        Assert.Equal(BackendProtocol.ChatCompletionsImageJson, reloaded.SelectedProtocol);
    }

    [Fact]
    public void Save_persists_routin_video_profile_separately_from_default_profile()
    {
        var repository = CreateRepository();
        repository.Upsert(new BackendProfile
        {
            Id = "default",
            Name = "OpenAI",
            BaseUrl = "https://api.openai.com/v1",
            ApiKey = "sk-openai",
            Protocol = BackendProtocol.OpenAiImages,
            MainlineModel = "gpt-4o-mini",
            ImageModel = "gpt-image-2",
            Concurrency = 1,
            Priority = 0,
            IsEnabled = true
        });
        var viewModel = new SettingsPageViewModel(repository)
        {
            SelectedProfileRole = BackendProfileRole.Video,
            SelectedProtocol = BackendProtocol.RoutinXaiVideo,
            ApiKey = "ak-video",
            VideoModel = "grok-imagine-video"
        };

        viewModel.SaveCommand.Execute(null);

        var defaultProfile = repository.GetById("default")!;
        var videoProfile = repository.GetById("video-default")!;
        Assert.Equal("sk-openai", defaultProfile.ApiKey);
        Assert.Equal(BackendProtocol.OpenAiImages, defaultProfile.Protocol);
        Assert.Equal("ak-video", videoProfile.ApiKey);
        Assert.Equal(BackendProtocol.RoutinXaiVideo, videoProfile.Protocol);
        Assert.Equal("https://api.routin.ai", videoProfile.BaseUrl);
        Assert.Equal("grok-imagine-video", videoProfile.VideoModel);
        Assert.True(videoProfile.SupportsVideoGeneration);
        Assert.False(videoProfile.SupportsImageGeneration);
    }

    [Fact]
    public void Selecting_routin_protocol_without_existing_profile_clears_previous_api_key()
    {
        var repository = CreateRepository();
        repository.Upsert(new BackendProfile
        {
            Id = "default",
            Name = "OpenAI",
            BaseUrl = "https://api.openai.com/v1",
            ApiKey = "sk-openai",
            Protocol = BackendProtocol.OpenAiImages,
            MainlineModel = "gpt-4o-mini",
            ImageModel = "gpt-image-2",
            Concurrency = 1,
            Priority = 0,
            IsEnabled = true
        });
        var viewModel = new SettingsPageViewModel(repository);

        viewModel.SelectedProtocol = BackendProtocol.RoutinXaiVideo;

        Assert.Equal("", viewModel.ApiKey);
        Assert.Equal("https://api.routin.ai", viewModel.BaseUrl);
        Assert.Equal("grok-imagine-video", viewModel.VideoModel);
    }

    [Fact]
    public void Selecting_openai_protocol_without_existing_default_profile_clears_routin_values()
    {
        var repository = CreateRepository();
        repository.Upsert(new BackendProfile
        {
            Id = "routin-video",
            Name = "Routin xAI Video",
            BaseUrl = "https://api.routin.ai",
            ApiKey = "ak-video",
            Protocol = BackendProtocol.RoutinXaiVideo,
            MainlineModel = "gpt-4o-mini",
            ImageModel = "gpt-image-2",
            VideoModel = "grok-imagine-video",
            Concurrency = 1,
            Priority = 0,
            IsEnabled = true
        });
        var viewModel = new SettingsPageViewModel(repository)
        {
            SelectedProfileRole = BackendProfileRole.Video,
            SelectedProtocol = BackendProtocol.RoutinXaiVideo,
            ApiKey = "ak-video"
        };

        viewModel.SelectedProfileRole = BackendProfileRole.Image;

        Assert.Equal("", viewModel.ApiKey);
        Assert.Equal("https://api.openai.com/v1", viewModel.BaseUrl);
        Assert.Equal("图像创作 API", viewModel.Name);
        Assert.Equal(BackendProtocol.OpenAiImages, viewModel.SelectedProtocol);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private BackendProfileRepository CreateRepository()
    {
        var database = new SqliteDatabase(AppPaths.CreateForRoot(_root));
        new SqliteSchemaInitializer(database).Initialize();
        return new BackendProfileRepository(database, new PassThroughSecretProtector());
    }

    private sealed class PassThroughSecretProtector : ISecretProtector
    {
        public string Protect(string value) => value;

        public string Unprotect(string protectedValue) => protectedValue;
    }
}
