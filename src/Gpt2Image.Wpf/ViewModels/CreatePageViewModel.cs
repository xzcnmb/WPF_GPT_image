using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gpt2Image.Core.Api;
using Gpt2Image.Core.Models;
using Gpt2Image.Core.Queue;
using Gpt2Image.Core.Storage;
using Gpt2Image.Core.Storage.Repositories;
using Microsoft.Extensions.Logging;

namespace Gpt2Image.Wpf.ViewModels;

public sealed partial class CreatePageViewModel : ObservableObject
{
    private readonly BackendProfileRepository _profiles;
    private readonly GenerationTaskRepository _tasks;
    private readonly LocalImageStorage _images;
    private readonly IImageGenerationClient _client;
    private readonly IGenerationQueue _queue;
    private readonly ILogger<CreatePageViewModel> _logger;

    [ObservableProperty]
    private string _prompt = "";

    [ObservableProperty]
    private string _size = "1024x1024";

    [ObservableProperty]
    private string _quality = "auto";

    [ObservableProperty]
    private string _responseFormat = "url";

    [ObservableProperty]
    private int _count = 1;

    [ObservableProperty]
    private string _status = "";

    [ObservableProperty]
    private bool _isGenerating;

    [ObservableProperty]
    private string _currentTaskId = "";

    public CreatePageViewModel(
        BackendProfileRepository profiles,
        GenerationTaskRepository tasks,
        LocalImageStorage images,
        IImageGenerationClient client,
        IGenerationQueue queue,
        ILogger<CreatePageViewModel> logger)
    {
        _profiles = profiles;
        _tasks = tasks;
        _images = images;
        _client = client;
        _queue = queue;
        _logger = logger;
    }

    public string[] Sizes { get; } = new[] { "1024x1024", "1536x1024", "1024x1536", "3840x2048", "auto" };
    public string[] Qualities { get; } = new[] { "auto", "low", "medium", "high" };
    public string[] ResponseFormats { get; } = new[] { "url", "b64_json" };
    public ObservableCollection<GenerationPreviewItemViewModel> PreviewImages { get; } = new();
    public ObservableCollection<RunLogEntryViewModel> RunLogs { get; } = new();

    [RelayCommand]
    private async Task GenerateAsync(CancellationToken cancellationToken)
    {
        var profile = _profiles.ListEnabled().FirstOrDefault();
        if (profile is null)
        {
            Status = "缺少后端配置";
            AddLog("警告", "缺少后端配置", "设置页保存接口地址和密钥。");
            return;
        }

        if (string.IsNullOrWhiteSpace(Prompt))
        {
            Status = "提示词为空";
            AddLog("警告", "提示词为空");
            return;
        }

        var requestedCount = Math.Clamp(Count, 1, 8);
        if (requestedCount != Count)
        {
            Count = requestedCount;
        }

        var taskId = Guid.NewGuid().ToString("N");
        CurrentTaskId = taskId;
        PreviewImages.Clear();
        RunLogs.Clear();
        for (var index = 0; index < requestedCount; index++)
        {
            PreviewImages.Add(new GenerationPreviewItemViewModel(index)
            {
                Status = "等待"
            });
        }

        var request = new ImageGenerationRequest
        {
            Prompt = Prompt,
            Size = Size,
            Quality = Quality,
            ResponseFormat = ResponseFormat,
            Count = requestedCount,
            OutputFormat = "png"
        };

        IsGenerating = true;
        Status = "准备中";
        AddLog("信息", "任务已创建", $"任务 {taskId[..8]}，模型 {profile.ImageModel}，协议 {BackendProtocol.DisplayName(profile.Protocol)}，数量 {requestedCount}。");

        _tasks.CreateTask(new GenerationTaskRecord
        {
            Id = taskId,
            Mode = "generate",
            Prompt = Prompt,
            ParametersJson = JsonSerializer.Serialize(request),
            Status = "pending",
            BackendProfileId = profile.Id,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });

        try
        {
            Status = "生成中";
            AddLog("信息", "请求已发送", $"Base URL: {profile.BaseUrl}，协议 {BackendProtocol.DisplayName(profile.Protocol)}，尺寸 {Size}，质量 {Quality}，返回格式 {ResponseFormat}。");
            SetAllLoadingStatus("生成中");

            var result = await _queue.EnqueueAsync(profile.Id, profile.Priority, async token =>
            {
                _tasks.MarkRunning(taskId);
                return await _client.GenerateAsync(profile, request, token);
            }, cancellationToken);

            if (result.Error is not null)
            {
                MarkAllFailed($"失败：{result.Error}");
                _tasks.MarkFailed(taskId, result.Error);
                Status = $"失败：{result.Error}";
                AddLog("错误", "失败", result.Error);
                return;
            }

            var base64Outputs = result.Images
                .Where(image => !string.IsNullOrWhiteSpace(image.Base64))
                .ToList();

            if (base64Outputs.Count == 0)
            {
                const string error = "未返回图片";
                MarkAllFailed(error);
                _tasks.MarkFailed(taskId, error);
                Status = error;
                AddLog("错误", "未收到图片", "后端响应中没有 b64_json、result、data URL 或兼容图片 URL，可能接口协议选错。");
                return;
            }

            foreach (var output in base64Outputs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var preview = GetOrCreatePreview(output.Index);
                preview.IsLoading = false;
                preview.PreviewBase64 = output.Base64;
                preview.Status = "保存中";
                AddLog("信息", "已预览", $"图片 {output.Index + 1}。{output.RevisedPrompt}");

                var saved = _images.SaveBase64Image(taskId, output.Index, output.Base64!, request.OutputFormat, ImageOutputRole.Final);
                preview.FilePath = saved.FilePath;
                preview.Status = "已保存";
                _tasks.AddOutput(taskId, new GenerationOutputRecord
                {
                    OutputIndex = output.Index,
                    OutputRole = saved.OutputRole,
                    FilePath = saved.FilePath,
                    MimeType = saved.MimeType,
                    Sha256 = saved.Sha256,
                    RevisedPrompt = output.RevisedPrompt,
                    CreatedAt = DateTimeOffset.UtcNow
                });
                AddLog("信息", "已保存", $"图片 {output.Index + 1}: {saved.FilePath}");
            }

            _tasks.MarkCompleted(taskId);
            Status = $"完成：{base64Outputs.Count} 张";
            AddLog("信息", "任务完成", $"任务 {taskId[..8]}，{base64Outputs.Count} 张图片。");
        }
        catch (OperationCanceledException)
        {
            const string message = "已取消";
            MarkAllFailed(message);
            _tasks.MarkFailed(taskId, message);
            Status = message;
            AddLog("警告", message, $"任务 {taskId[..8]}");
        }
        catch (Exception ex)
        {
            MarkAllFailed(ex.Message);
            _tasks.MarkFailed(taskId, ex.Message);
            Status = $"异常：{ex.Message}";
            AddLog("错误", "异常", ex.ToString());
        }
        finally
        {
            IsGenerating = false;
        }
    }

    private GenerationPreviewItemViewModel GetOrCreatePreview(int index)
    {
        var preview = PreviewImages.FirstOrDefault(item => item.Index == index);
        if (preview is not null)
        {
            return preview;
        }

        preview = new GenerationPreviewItemViewModel(index);
        PreviewImages.Add(preview);
        return preview;
    }

    private void SetAllLoadingStatus(string status)
    {
        foreach (var preview in PreviewImages)
        {
            preview.IsLoading = true;
            preview.HasError = false;
            preview.Status = status;
        }
    }

    private void MarkAllFailed(string status)
    {
        foreach (var preview in PreviewImages)
        {
            if (!preview.HasImage)
            {
                preview.IsLoading = false;
                preview.HasError = true;
                preview.Status = status;
            }
        }
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
}
