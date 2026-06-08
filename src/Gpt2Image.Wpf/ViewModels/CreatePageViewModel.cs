using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gpt2Image.Core.Api;
using Gpt2Image.Core.Models;
using Gpt2Image.Core.Queue;
using Gpt2Image.Core.Storage;
using Gpt2Image.Core.Storage.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace Gpt2Image.Wpf.ViewModels;

public sealed partial class CreatePageViewModel : ObservableObject
{
    private readonly BackendProfileRepository _profiles;
    private readonly GenerationTaskRepository _tasks;
    private readonly InputAssetRepository _inputAssets;
    private readonly LocalImageStorage _images;
    private readonly IImageGenerationClient _client;
    private readonly IGenerationQueue _queue;
    private readonly ILogger<CreatePageViewModel> _logger;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OptimizePromptCommand))]
    private string _prompt = "";

    [ObservableProperty]
    private string _size = "1024x1024";

    [ObservableProperty]
    private string _quality = "auto";

    [ObservableProperty]
    private string _responseFormat = "b64_json";

    [ObservableProperty]
    private int _count = 1;

    [ObservableProperty]
    private string _status = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OptimizePromptCommand))]
    private bool _isGenerating;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OptimizePromptCommand))]
    private bool _isOptimizingPrompt;

    [ObservableProperty]
    private string _currentTaskId = "";

    [ObservableProperty]
    private InputAssetViewModel? _maskImage;

    public CreatePageViewModel(
        BackendProfileRepository profiles,
        GenerationTaskRepository tasks,
        InputAssetRepository inputAssets,
        LocalImageStorage images,
        IImageGenerationClient client,
        IGenerationQueue queue,
        ILogger<CreatePageViewModel> logger)
    {
        _profiles = profiles;
        _tasks = tasks;
        _inputAssets = inputAssets;
        _images = images;
        _client = client;
        _queue = queue;
        _logger = logger;
    }

    public string[] Sizes { get; } = new[] { "1024x1024", "1536x1024", "1024x1536", "3840x2048", "auto" };
    public string[] Qualities { get; } = new[] { "auto", "low", "medium", "high" };
    public string[] ResponseFormats { get; } = new[] { "b64_json", "url" };
    public ObservableCollection<GenerationPreviewItemViewModel> PreviewImages { get; } = new();
    public ObservableCollection<InputAssetViewModel> InputImages { get; } = new();
    public ObservableCollection<RunLogEntryViewModel> RunLogs { get; } = new();

    [RelayCommand]
    private void AddInputImages()
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择参考图或编辑图",
            Filter = "图片文件|*.png;*.jpg;*.jpeg;*.webp|PNG|*.png|JPEG|*.jpg;*.jpeg|WebP|*.webp|所有文件|*.*",
            Multiselect = true,
            CheckFileExists = true
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        foreach (var fileName in dialog.FileNames)
        {
            AddInputAsset(fileName, isMask: false);
        }
    }

    [RelayCommand]
    private void RemoveInputImage(InputAssetViewModel? asset)
    {
        if (asset is not null)
        {
            InputImages.Remove(asset);
        }
    }

    [RelayCommand]
    private void ClearInputImages() => InputImages.Clear();

    [RelayCommand]
    private void SelectMaskImage()
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择编辑蒙版",
            Filter = "图片文件|*.png;*.jpg;*.jpeg;*.webp|PNG|*.png|JPEG|*.jpg;*.jpeg|WebP|*.webp|所有文件|*.*",
            Multiselect = false,
            CheckFileExists = true
        };

        if (dialog.ShowDialog() == true)
        {
            AddInputAsset(dialog.FileName, isMask: true);
        }
    }

    [RelayCommand]
    private void ClearMaskImage() => MaskImage = null;

    [RelayCommand(CanExecute = nameof(CanOptimizePrompt))]
    private async Task OptimizePromptAsync(CancellationToken cancellationToken)
    {
        var profile = _profiles.GetFirstEnabledForRole(BackendProfileRole.Prompt);
        if (profile is null)
        {
            Status = "缺少提示词润色后端配置";
            AddLog("警告", "缺少提示词润色后端配置", "请在设置页保存用途为提示词润色 / 对话 API 的配置。");
            return;
        }

        var prompt = Prompt.Trim();
        if (string.IsNullOrWhiteSpace(prompt))
        {
            Status = "提示词为空";
            AddLog("警告", "提示词为空");
            return;
        }

        try
        {
            IsOptimizingPrompt = true;
            Status = "正在优化提示词";
            AddLog("信息", "开始优化提示词", $"使用主模型 {profile.MainlineModel} 润色为专业图片提示词。");

            var result = await _client.OptimizePromptAsync(profile, prompt, cancellationToken);
            if (!string.IsNullOrWhiteSpace(result.Error))
            {
                Status = $"优化失败：{result.Error}";
                AddLog("错误", "优化失败", result.Error);
                return;
            }

            if (string.IsNullOrWhiteSpace(result.OptimizedPrompt))
            {
                const string error = "优化结果为空";
                Status = error;
                AddLog("错误", error);
                return;
            }

            Prompt = result.OptimizedPrompt.Trim();
            Status = "提示词已优化";
            AddLog("信息", "提示词已优化", Prompt);
        }
        catch (OperationCanceledException)
        {
            Status = "提示词优化已取消";
            AddLog("警告", "提示词优化已取消");
        }
        catch (Exception ex)
        {
            Status = $"优化异常：{ex.Message}";
            AddLog("错误", "优化异常", ex.ToString());
        }
        finally
        {
            IsOptimizingPrompt = false;
        }
    }

    [RelayCommand]
    private async Task GenerateAsync(CancellationToken cancellationToken)
    {
        if (IsOptimizingPrompt)
        {
            Status = "提示词优化中";
            return;
        }

        var profile = _profiles.GetFirstEnabledForRole(BackendProfileRole.Image);
        if (profile is null)
        {
            Status = "缺少图片生成后端配置";
            AddLog("警告", "缺少图片生成后端配置", "请在设置页保存用途为图片生成 API 的配置。");
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
            PreviewImages.Add(CreatePreviewItem(taskId, index, "等待"));
        }

        var responseFormat = ResolveResponseFormat(ResponseFormat);
        var inputImages = InputImages.Select(asset => asset.ToModel()).ToList();
        var mask = MaskImage?.ToModel();
        var mode = inputImages.Count > 0 || mask is not null ? "edit" : "generate";
        if (mask is not null && inputImages.Count == 0)
        {
            Status = "请先添加输入图片，再使用蒙版编辑";
            AddLog("警告", "缺少输入图片", "蒙版需要配合至少一张输入图片使用。 ");
            return;
        }

        var missingAsset = inputImages.Concat(mask is null ? Array.Empty<ImageInputAsset>() : new[] { mask })
            .FirstOrDefault(asset => !File.Exists(asset.FilePath));
        if (missingAsset is not null)
        {
            Status = $"输入图片不存在：{missingAsset.FilePath}";
            AddLog("错误", "输入图片不存在", missingAsset.FilePath);
            return;
        }

        var request = new ImageGenerationRequest
        {
            Prompt = Prompt,
            Images = inputImages,
            Mask = mask,
            Mode = mode,
            Model = profile.ImageModel,
            Size = Size,
            Quality = Quality,
            ResponseFormat = responseFormat,
            Count = requestedCount,
            OutputFormat = "png"
        };

        IsGenerating = true;
        Status = "准备中";
        AddLog("信息", "任务已创建", $"任务 {taskId[..8]}，模式 {mode}，模型 {profile.ImageModel}，协议 {BackendProtocol.DisplayName(profile.Protocol)}，数量 {requestedCount}，输入图 {inputImages.Count} 张，蒙版 {(mask is null ? "无" : "有")}。");

        _tasks.CreateTask(new GenerationTaskRecord
        {
            Id = taskId,
            Mode = mode,
            Prompt = Prompt,
            ParametersJson = JsonSerializer.Serialize(request),
            Status = "pending",
            BackendProfileId = profile.Id,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        foreach (var asset in InputImages.Concat(MaskImage is null ? Array.Empty<InputAssetViewModel>() : new[] { MaskImage }))
        {
            _inputAssets.Add(new InputAssetRecord
            {
                TaskId = taskId,
                FilePath = asset.FilePath,
                MimeType = asset.MimeType,
                Sha256 = asset.Sha256,
                CreatedAt = DateTimeOffset.UtcNow
            });
        }

        try
        {
            Status = "生成中";
            AddLog("信息", "请求已发送", $"Base URL: {profile.BaseUrl}，协议 {BackendProtocol.DisplayName(profile.Protocol)}，模式 {mode}，尺寸 {Size}，质量 {Quality}，返回格式 {responseFormat}，输入图 {inputImages.Count} 张。");
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

            var imageOutputs = result.Images
                .Where(image => !string.IsNullOrWhiteSpace(image.Base64) || !string.IsNullOrWhiteSpace(image.Url))
                .ToList();

            if (imageOutputs.Count == 0)
            {
                const string error = "未返回图片";
                MarkAllFailed(error);
                _tasks.MarkFailed(taskId, error);
                Status = error;
                AddLog("错误", "未收到图片", "后端响应中没有 b64_json、result、data URL 或兼容图片 URL，可能接口协议选错。");
                return;
            }

            foreach (var output in imageOutputs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                StoredImageOutput? saved = null;
                if (!string.IsNullOrWhiteSpace(output.Base64))
                {
                    saved = _images.SaveBase64Image(taskId, output.Index, output.Base64!, request.OutputFormat, ImageOutputRole.Final);
                }

                var preview = GetOrCreatePreview(taskId, output.Index);
                preview.IsLoading = false;
                preview.PreviewBase64 = output.Base64;
                preview.SourceUrl = output.Url;
                preview.FilePath = saved?.FilePath;
                preview.Status = saved is not null
                    ? $"已生成并保存到 {Path.GetFileName(saved.FilePath)}"
                    : "已生成 URL，右键或点击按钮保存";
                AddLog("信息", "已生成", $"图片 {output.Index + 1}。{output.RevisedPrompt}");

                _tasks.AddOutput(taskId, new GenerationOutputRecord
                {
                    OutputIndex = output.Index,
                    OutputRole = saved?.OutputRole ?? ImageOutputRole.Final.ToString().ToLowerInvariant(),
                    FilePath = saved?.FilePath ?? "",
                    MimeType = saved?.MimeType ?? MimeTypeFor(request.OutputFormat),
                    Sha256 = saved?.Sha256 ?? (!string.IsNullOrWhiteSpace(output.Base64)
                        ? Sha256ForBase64(output.Base64!)
                        : Sha256ForText(output.Url!)),
                    RevisedPrompt = output.RevisedPrompt,
                    ImageBase64 = output.Base64,
                    SourceUrl = output.Url,
                    CreatedAt = DateTimeOffset.UtcNow
                });
                AddLog("信息", saved is null ? "已入库" : "已保存", saved is null
                    ? $"图片 {output.Index + 1} 已保存到历史任务。"
                    : $"图片 {output.Index + 1} 已保存到 {saved.FilePath} 并写入历史任务。");
            }

            _tasks.MarkCompleted(taskId);
            Status = $"完成：{imageOutputs.Count} 张";
            AddLog("信息", "任务完成", $"任务 {taskId[..8]}，{imageOutputs.Count} 张图片。");
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

    private void AddInputAsset(string sourcePath, bool isMask)
    {
        try
        {
            ValidateInputImage(sourcePath, isMask);
            var stored = _images.SaveInputAsset(sourcePath);
            if (!isMask && InputImages.Any(asset => string.Equals(asset.Sha256, stored.Sha256, StringComparison.OrdinalIgnoreCase)))
            {
                AddLog("信息", "图片已存在", Path.GetFileName(sourcePath));
                return;
            }

            var viewModel = new InputAssetViewModel
            {
                FilePath = stored.FilePath,
                MimeType = stored.MimeType,
                Sha256 = stored.Sha256,
                ByteLength = stored.ByteLength
            };

            if (isMask)
            {
                MaskImage = viewModel;
                AddLog("信息", "已选择蒙版", Path.GetFileName(sourcePath));
            }
            else
            {
                InputImages.Add(viewModel);
                AddLog("信息", "已添加输入图片", Path.GetFileName(sourcePath));
            }
        }
        catch (Exception ex)
        {
            Status = $"添加图片失败：{ex.Message}";
            AddLog("错误", "添加图片失败", ex.Message);
        }
    }

    private static void ValidateInputImage(string sourcePath, bool isMask)
    {
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("图片文件不存在。", sourcePath);
        }

        var extension = Path.GetExtension(sourcePath).TrimStart('.').ToLowerInvariant();
        if (extension is not ("png" or "jpg" or "jpeg" or "webp"))
        {
            throw new InvalidOperationException("仅支持 PNG、JPEG 或 WebP 图片。 ");
        }

        var length = new FileInfo(sourcePath).Length;
        const long maxBytes = 25L * 1024 * 1024;
        if (length > maxBytes)
        {
            throw new InvalidOperationException("图片不能超过 25MB。 ");
        }

        if (isMask && extension != "png")
        {
            throw new InvalidOperationException("蒙版建议使用 PNG 图片。 ");
        }
    }

    private bool CanOptimizePrompt() => !IsGenerating && !IsOptimizingPrompt && !string.IsNullOrWhiteSpace(Prompt);

    private static string ResolveResponseFormat(string responseFormat)
    {
        return string.Equals(responseFormat, "b64_json", StringComparison.OrdinalIgnoreCase)
            ? "b64_json"
            : "url";
    }

    private GenerationPreviewItemViewModel CreatePreviewItem(string taskId, int index, string status)
    {
        return new GenerationPreviewItemViewModel(index)
        {
            Status = status,
            SavedFilePathChanged = filePath => _tasks.UpdateOutputFilePath(taskId, index, filePath)
        };
    }

    private GenerationPreviewItemViewModel GetOrCreatePreview(string taskId, int index)
    {
        var preview = PreviewImages.FirstOrDefault(item => item.Index == index);
        if (preview is not null)
        {
            return preview;
        }

        preview = CreatePreviewItem(taskId, index, "等待");
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

    private static string Sha256ForBase64(string base64)
    {
        var commaIndex = base64.IndexOf(',');
        if (commaIndex >= 0)
        {
            base64 = base64[(commaIndex + 1)..];
        }

        return Convert.ToHexString(SHA256.HashData(Convert.FromBase64String(base64)));
    }

    private static string Sha256ForText(string value)
    {
        return Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));
    }

    private static string MimeTypeFor(string outputFormat)
    {
        return outputFormat.Trim().TrimStart('.').ToLowerInvariant() switch
        {
            "jpg" or "jpeg" => "image/jpeg",
            "webp" => "image/webp",
            _ => "image/png"
        };
    }
}
