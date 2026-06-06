using Gpt2Image.Core.Api;
using Gpt2Image.Core.Models;
using Gpt2Image.Core.Queue;
using Gpt2Image.Core.Security;
using Gpt2Image.Core.Storage;
using Gpt2Image.Core.Storage.Repositories;
using Gpt2Image.Wpf.ViewModels;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace Gpt2Image.Tests.Wpf;

public sealed class CreatePageViewModelTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "gpt2image-create-vm-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task GenerateAsync_shows_loading_preview_then_updates_preview_and_run_log()
    {
        var paths = AppPaths.CreateForRoot(_root);
        var database = new SqliteDatabase(paths);
        new SqliteSchemaInitializer(database).Initialize();
        var profiles = new BackendProfileRepository(database, new PassThroughSecretProtector());
        profiles.Upsert(new BackendProfile
        {
            Id = "default",
            Name = "测试后端",
            BaseUrl = "https://example.test/v1",
            ApiKey = "sk-test",
            MainlineModel = "gpt-5.5",
            ImageModel = "gpt-image-2",
            Concurrency = 1,
            Priority = 0,
            IsEnabled = true
        });
        var taskRepository = new GenerationTaskRepository(database);
        var inputAssetRepository = new InputAssetRepository(database);
        var stubClient = new StubImageGenerationClient();
        var viewModel = new CreatePageViewModel(
            profiles,
            taskRepository,
            inputAssetRepository,
            new LocalImageStorage(paths, new FixedClock(new DateTimeOffset(2026, 6, 6, 12, 0, 0, TimeSpan.Zero))),
            stubClient,
            new GenerationQueue(new GenerationQueueOptions(1)),
            NullLogger<CreatePageViewModel>.Instance);

        viewModel.Prompt = "生成一个桌面工作台";
        viewModel.Count = 1;

        await viewModel.GenerateCommand.ExecuteAsync(null);

        var preview = Assert.Single(viewModel.PreviewImages);
        Assert.False(preview.IsLoading);
        Assert.True(preview.HasImage);
        Assert.True(preview.IsSaved);
        Assert.EndsWith(Path.Combine("images", "2026", "06", "06", preview.FileName), preview.FilePath);
        Assert.Contains(viewModel.RunLogs, log => log.Message.Contains("请求已发送", StringComparison.Ordinal));
        Assert.Contains(viewModel.RunLogs, log => log.Message.Contains("已保存", StringComparison.Ordinal));
        Assert.Equal("generate", stubClient.LastRequest?.Mode);
        Assert.Empty(inputAssetRepository.ListByTaskId(viewModel.CurrentTaskId));
    }

    [Fact]
    public async Task GenerateAsync_with_input_image_switches_to_edit_mode_and_persists_input_asset()
    {
        var paths = AppPaths.CreateForRoot(_root);
        var database = new SqliteDatabase(paths);
        new SqliteSchemaInitializer(database).Initialize();
        var profiles = new BackendProfileRepository(database, new PassThroughSecretProtector());
        profiles.Upsert(new BackendProfile
        {
            Id = "default",
            Name = "测试后端",
            BaseUrl = "https://example.test/v1",
            ApiKey = "sk-test",
            MainlineModel = "gpt-5.5",
            ImageModel = "gpt-image-2",
            Concurrency = 1,
            Priority = 0,
            IsEnabled = true
        });
        var inputAssetRepository = new InputAssetRepository(database);
        var stubClient = new StubImageGenerationClient();
        var sourceImagePath = Path.Combine(_root, "input.png");
        File.WriteAllBytes(sourceImagePath, new byte[] { 1, 2, 3, 4 });
        var viewModel = new CreatePageViewModel(
            profiles,
            new GenerationTaskRepository(database),
            inputAssetRepository,
            new LocalImageStorage(paths, new FixedClock(new DateTimeOffset(2026, 6, 6, 12, 0, 0, TimeSpan.Zero))),
            stubClient,
            new GenerationQueue(new GenerationQueueOptions(1)),
            NullLogger<CreatePageViewModel>.Instance);
        viewModel.InputImages.Add(new InputAssetViewModel
        {
            FilePath = sourceImagePath,
            MimeType = "image/png",
            Sha256 = "hash",
            ByteLength = 4
        });
        viewModel.Prompt = "把背景改成海边日落";

        await viewModel.GenerateCommand.ExecuteAsync(null);

        Assert.Equal("edit", stubClient.LastRequest?.Mode);
        Assert.Single(stubClient.LastRequest?.Images ?? Array.Empty<ImageInputAsset>());
        Assert.Single(inputAssetRepository.ListByTaskId(viewModel.CurrentTaskId));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class StubImageGenerationClient : IImageGenerationClient
    {
        public ImageGenerationRequest? LastRequest { get; private set; }

        public Task<GenerationResult> GenerateAsync(BackendProfile profile, ImageGenerationRequest request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new GenerationResult
            {
                Images = new[]
                {
                    new GeneratedImageOutput
                    {
                        Base64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/p9sAAAAASUVORK5CYII=",
                        Index = 0,
                        RevisedPrompt = "生成一个桌面工作台"
                    }
                }
            });
        }

        public async IAsyncEnumerable<ImageStreamEvent> StreamAgentImagesAsync(
            BackendProfile profile,
            AgentRunRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class FixedClock : IClock
    {
        private readonly DateTimeOffset _now;

        public FixedClock(DateTimeOffset now)
        {
            _now = now;
        }

        public DateTimeOffset UtcNow => _now;
    }

    private sealed class PassThroughSecretProtector : ISecretProtector
    {
        public string Protect(string value) => value;

        public string Unprotect(string protectedValue) => protectedValue;
    }
}
