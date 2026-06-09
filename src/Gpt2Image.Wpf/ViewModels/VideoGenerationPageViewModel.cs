using System.Collections.ObjectModel;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gpt2Image.Core.Api;
using Gpt2Image.Core.Models;
using Gpt2Image.Core.Queue;
using Gpt2Image.Core.Storage.Repositories;
using Microsoft.Extensions.Logging;

namespace Gpt2Image.Wpf.ViewModels;

public sealed partial class VideoGenerationPageViewModel : ObservableObject
{
    private static readonly HashSet<string> AllowedAspectRatios = new(StringComparer.OrdinalIgnoreCase)
    {
        "16:9", "9:16", "1:1", "4:3", "3:4", "3:2", "2:3"
    };

    private static readonly HashSet<string> AllowedResolutions = new(StringComparer.OrdinalIgnoreCase)
    {
        "480p", "720p"
    };

    private readonly BackendProfileRepository _profiles;
    private readonly GenerationTaskRepository _tasks;
    private readonly IVideoGenerationClient _client;
    private readonly IImageGenerationClient _promptClient;
    private readonly IGenerationQueue _queue;
    private readonly ILogger<VideoGenerationPageViewModel> _logger;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GenerateVideoCommand))]
    [NotifyCanExecuteChangedFor(nameof(OptimizePromptCommand))]
    private string _prompt = "";

    [ObservableProperty]
    private int _duration = 6;

    [ObservableProperty]
    private string _aspectRatio = "16:9";

    [ObservableProperty]
    private string _resolution = "720p";

    [ObservableProperty]
    private string _imageUrl = "";

    [ObservableProperty]
    private string _referenceImageUrlsText = "";

    [ObservableProperty]
    private string _status = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GenerateVideoCommand))]
    [NotifyCanExecuteChangedFor(nameof(OptimizePromptCommand))]
    private bool _isGenerating;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GenerateVideoCommand))]
    [NotifyCanExecuteChangedFor(nameof(OptimizePromptCommand))]
    private bool _isOptimizingPrompt;

    [ObservableProperty]
    private string _currentTaskId = "";

    public VideoGenerationPageViewModel(
        BackendProfileRepository profiles,
        GenerationTaskRepository tasks,
        IVideoGenerationClient client,
        IImageGenerationClient promptClient,
        IGenerationQueue queue,
        ILogger<VideoGenerationPageViewModel> logger)
    {
        _profiles = profiles;
        _tasks = tasks;
        _client = client;
        _promptClient = promptClient;
        _queue = queue;
        _logger = logger;
    }

    public string[] AspectRatios { get; } = AllowedAspectRatios.ToArray();
    public string[] Resolutions { get; } = AllowedResolutions.ToArray();
    public ObservableCollection<GenerationPreviewItemViewModel> PreviewVideos { get; } = new();
    public ObservableCollection<RunLogEntryViewModel> RunLogs { get; } = new();

    [RelayCommand(CanExecute = nameof(CanOptimizePrompt))]
    private async Task OptimizePromptAsync(CancellationToken cancellationToken)
    {
        var prompt = Prompt.Trim();
        if (string.IsNullOrWhiteSpace(prompt))
        {
            Status = "提示词为空";
            AddLog("警告", "提示词为空");
            return;
        }

        var profile = ResolvePromptOptimizationProfile();
        if (profile is null)
        {
            Status = "缺少提示词润色后端配置";
            AddLog("警告", "缺少提示词润色后端配置", "请在设置页保存接口地址和密钥。 ");
            return;
        }

        try
        {
            IsOptimizingPrompt = true;
            Status = "正在润色视频提示词";
            AddLog("信息", "开始润色视频提示词", $"与图片生成保持一致，使用默认主模型 {profile.MainlineModel} 进行提示词润色。 ");

            var result = await _promptClient.OptimizeVideoPromptAsync(profile, prompt, cancellationToken);

            if (!string.IsNullOrWhiteSpace(result.Error))
            {
                Status = $"润色失败：{result.Error}";
                AddLog("错误", "润色失败", result.Error);
                return;
            }

            if (string.IsNullOrWhiteSpace(result.OptimizedPrompt))
            {
                const string error = "润色结果为空";
                Status = error;
                AddLog("错误", error);
                return;
            }

            Prompt = result.OptimizedPrompt.Trim();
            Status = "视频提示词已润色";
            AddLog("信息", "视频提示词已润色", Prompt);
        }
        catch (OperationCanceledException)
        {
            Status = "视频提示词润色已取消";
            AddLog("警告", "视频提示词润色已取消");
        }
        catch (Exception ex)
        {
            Status = $"润色异常：{ex.Message}";
            AddLog("错误", "润色异常", ex.ToString());
        }
        finally
        {
            IsOptimizingPrompt = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanGenerateVideo))]
    private async Task GenerateVideoAsync(CancellationToken cancellationToken)
    {
        if (IsOptimizingPrompt)
        {
            Status = "提示词润色中";
            return;
        }

        var profile = _profiles.GetFirstEnabledForRole(BackendProfileRole.Video);
        if (profile is null)
        {
            Status = "缺少视频生成后端配置";
            AddLog("警告", "缺少视频生成后端配置", "请在设置页保存用途为视频生成 API 的配置。 ");
            return;
        }

        if (!TryBuildRequest(profile, out var request, out var validationError))
        {
            Status = validationError;
            AddLog("警告", "参数校验失败", validationError);
            return;
        }

        var taskId = Guid.NewGuid().ToString("N");
        CurrentTaskId = taskId;
        PreviewVideos.Clear();
        RunLogs.Clear();
        var preview = CreatePreviewItem(taskId, 0, "等待提交");
        PreviewVideos.Add(preview);

        IsGenerating = true;
        Status = "准备中";
        AddLog("信息", "视频任务已创建", $"任务 {taskId[..8]}，模型 {request.Model}，比例 {request.AspectRatio}，分辨率 {request.Resolution}，时长 {request.Duration ?? 0} 秒。 ");

        _tasks.CreateTask(new GenerationTaskRecord
        {
            Id = taskId,
            Mode = "video",
            Prompt = request.Prompt,
            ParametersJson = JsonSerializer.Serialize(request),
            Status = "pending",
            BackendProfileId = profile.Id,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });

        try
        {
            Status = "提交中";
            preview.IsLoading = true;
            preview.Status = "提交到 Routin 视频接口";
            AddLog("信息", "请求已发送", $"Base URL: {profile.BaseUrl}，协议 {BackendProtocol.DisplayName(profile.Protocol)}。 ");

            var result = await _queue.EnqueueAsync(profile.Id, profile.Priority, async token =>
            {
                _tasks.MarkRunning(taskId);
                return await _client.GenerateAsync(profile, request, token);
            }, cancellationToken);

            if (!string.IsNullOrWhiteSpace(result.Error))
            {
                MarkFailed(preview, result.Error!);
                _tasks.MarkFailed(taskId, result.Error!);
                Status = $"失败：{result.Error}";
                AddLog("错误", "视频生成失败", result.Error);
                return;
            }

            var output = result.Videos.FirstOrDefault();
            if (output is null || (string.IsNullOrWhiteSpace(output.Url) && string.IsNullOrWhiteSpace(output.FilePath)))
            {
                const string error = "未返回可播放的视频地址";
                MarkFailed(preview, error);
                _tasks.MarkFailed(taskId, error);
                Status = error;
                AddLog("错误", "未收到视频", "Routin 响应中没有 video.url 或可播放的视频地址。 ");
                return;
            }

            var filePath = string.IsNullOrWhiteSpace(output.FilePath) ? "" : output.FilePath;
            var mimeType = output.MimeType ?? "video/mp4";
            var sha256 = output.Sha256 ?? Sha256ForText(output.Url ?? taskId);

            preview.IsLoading = false;
            preview.HasError = false;
            preview.FilePath = string.IsNullOrWhiteSpace(filePath) ? null : filePath;
            preview.SourceUrl = output.Url;
            preview.DurationSeconds = output.DurationSeconds;
            preview.Status = "视频已生成，可在线播放；如需本地文件请手动另存为。";

            _tasks.AddOutput(taskId, new GenerationOutputRecord
            {
                OutputIndex = output.Index,
                OutputRole = output.OutputRole,
                FilePath = filePath,
                MimeType = mimeType,
                Sha256 = sha256,
                SourceUrl = output.Url,
                MediaType = "video",
                DurationSeconds = output.DurationSeconds,
                ProviderRequestId = result.ProviderRequestId,
                MetadataJson = JsonSerializer.Serialize(new
                {
                    result.ProviderRequestId,
                    result.Progress,
                    output.DurationSeconds,
                    output.Url,
                    storage = "remote-url-only"
                }),
                CreatedAt = DateTimeOffset.UtcNow
            });

            _tasks.MarkCompleted(taskId);
            Status = "视频生成完成";
            AddLog("信息", "任务完成", $"任务 {taskId[..8]}，request_id {result.ProviderRequestId}，已保留远程视频链接，未自动保存。 ");
        }
        catch (OperationCanceledException)
        {
            const string message = "已取消";
            MarkFailed(preview, message);
            _tasks.MarkFailed(taskId, message);
            Status = message;
            AddLog("警告", message, $"任务 {taskId[..8]}");
        }
        catch (Exception ex)
        {
            MarkFailed(preview, ex.Message);
            _tasks.MarkFailed(taskId, ex.Message);
            Status = $"异常：{ex.Message}";
            AddLog("错误", "异常", ex.ToString());
        }
        finally
        {
            IsGenerating = false;
        }
    }

    private bool CanGenerateVideo() => !IsGenerating && !IsOptimizingPrompt && !string.IsNullOrWhiteSpace(Prompt);

    private bool CanOptimizePrompt() => !IsGenerating && !IsOptimizingPrompt && !string.IsNullOrWhiteSpace(Prompt);

    private BackendProfile? ResolvePromptOptimizationProfile()
    {
        return _profiles.GetFirstEnabledForRole(BackendProfileRole.Prompt);
    }

    private bool TryBuildRequest(BackendProfile profile, out VideoGenerationRequest request, out string error)
    {
        request = new VideoGenerationRequest();
        error = "";
        var prompt = Prompt.Trim();
        if (string.IsNullOrWhiteSpace(prompt))
        {
            error = "提示词为空";
            return false;
        }

        if (!AllowedAspectRatios.Contains(AspectRatio))
        {
            error = "视频比例不支持";
            return false;
        }

        if (!AllowedResolutions.Contains(Resolution))
        {
            error = "视频分辨率不支持";
            return false;
        }

        var imageUrl = ImageUrl.Trim();
        var referenceUrls = ParseReferenceUrls().ToList();
        if (!string.IsNullOrWhiteSpace(imageUrl) && referenceUrls.Count > 0)
        {
            error = "起始图片 URL 和参考图片 URL 不能同时使用";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(imageUrl) && !IsHttpUrl(imageUrl))
        {
            error = "起始图片 URL 必须是 http/https 地址";
            return false;
        }

        if (referenceUrls.Count > 7)
        {
            error = "参考图片最多 7 张";
            return false;
        }

        if (referenceUrls.Any(url => !IsHttpUrl(url)))
        {
            error = "参考图片 URL 必须全部是 http/https 地址";
            return false;
        }

        request = new VideoGenerationRequest
        {
            Prompt = prompt,
            Model = ResolveVideoModel(profile),
            Duration = Math.Clamp(Duration, 1, referenceUrls.Count > 0 ? 10 : 15),
            AspectRatio = AspectRatio,
            Resolution = Resolution,
            Image = string.IsNullOrWhiteSpace(imageUrl) ? null : new VideoInputReference { Url = imageUrl },
            ReferenceImages = referenceUrls.Select(url => new VideoInputReference { Url = url }).ToList()
        };
        return true;
    }

    private static string ResolveVideoModel(BackendProfile profile)
    {
        if (!string.IsNullOrWhiteSpace(profile.VideoModel))
        {
            return profile.VideoModel.Trim();
        }

        return string.IsNullOrWhiteSpace(profile.ImageModel) ? "grok-imagine-video" : profile.ImageModel.Trim();
    }

    private IEnumerable<string> ParseReferenceUrls()
    {
        return ReferenceImageUrlsText
            .Split(new[] { '\r', '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(item => item.Trim())
            .Where(item => !string.IsNullOrWhiteSpace(item));
    }

    private static bool IsHttpUrl(string value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    private GenerationPreviewItemViewModel CreatePreviewItem(string taskId, int index, string status)
    {
        var item = new GenerationPreviewItemViewModel(index)
        {
            MediaType = "video",
            Status = status,
            SavedFilePathChanged = filePath => _tasks.UpdateOutputFilePath(taskId, index, filePath)
        };
        item.SetTitle($"视频 {index + 1}");
        return item;
    }

    private static void MarkFailed(GenerationPreviewItemViewModel preview, string status)
    {
        preview.IsLoading = false;
        preview.HasError = true;
        preview.Status = status;
    }

    private void AddLog(string level, string message, string? detail = null)
    {
        var entry = new RunLogEntryViewModel(DateTimeOffset.Now, level, message, detail);
        RunLogs.Add(entry);

        switch (level)
        {
            case "错误":
                _logger.LogError("{Message} {Detail}", message, detail);
                break;
            case "警告":
                _logger.LogWarning("{Message} {Detail}", message, detail);
                break;
            default:
                _logger.LogInformation("{Message} {Detail}", message, detail);
                break;
        }
    }

    private static string Sha256ForText(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }
}
