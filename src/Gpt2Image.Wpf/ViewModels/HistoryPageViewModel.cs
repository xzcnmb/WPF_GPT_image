using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gpt2Image.Core.Storage.Repositories;

namespace Gpt2Image.Wpf.ViewModels;

public sealed partial class HistoryPageViewModel : ObservableObject
{
    private readonly GenerationTaskRepository _tasks;

    public HistoryPageViewModel(GenerationTaskRepository tasks)
    {
        _tasks = tasks;
        Refresh();
    }

    [ObservableProperty]
    private string _status = "";

    [ObservableProperty]
    private HistoryTaskItemViewModel? _selectedTask;

    public ObservableCollection<HistoryTaskItemViewModel> Tasks { get; } = new();

    public ObservableCollection<GenerationPreviewItemViewModel> Outputs { get; } = new();

    public void RefreshHistory() => Refresh();

    [RelayCommand]
    private void Refresh()
    {
        Tasks.Clear();
        foreach (var task in _tasks.ListRecent())
        {
            Tasks.Add(new HistoryTaskItemViewModel(task));
        }

        Status = Tasks.Count == 0 ? "暂无历史任务" : $"共 {Tasks.Count} 个历史任务";
        SelectedTask = Tasks.FirstOrDefault();
    }

    partial void OnSelectedTaskChanged(HistoryTaskItemViewModel? value)
    {
        Outputs.Clear();
        if (value is null)
        {
            return;
        }

        foreach (var output in _tasks.ListOutputs(value.Id))
        {
            var mediaType = string.IsNullOrWhiteSpace(output.MediaType) ? "image" : output.MediaType;
            var preview = new GenerationPreviewItemViewModel(output.OutputIndex)
            {
                MediaType = mediaType,
                PreviewBase64 = string.Equals(mediaType, "image", StringComparison.OrdinalIgnoreCase) ? ResolvePreviewBase64(output) : null,
                SourceUrl = output.SourceUrl,
                FilePath = File.Exists(output.FilePath) ? output.FilePath : null,
                DurationSeconds = output.DurationSeconds,
                IsLoading = false,
                Status = output.RevisedPrompt ?? BuildOutputStatus(mediaType, output),
                SavedFilePathChanged = filePath => _tasks.UpdateOutputFilePath(value.Id, output.OutputIndex, filePath)
            };
            if (string.Equals(mediaType, "video", StringComparison.OrdinalIgnoreCase))
            {
                preview.SetTitle($"视频 {output.OutputIndex + 1}");
            }

            Outputs.Add(preview);
        }
    }

    private static string BuildOutputStatus(string mediaType, GenerationTaskOutputRecord output)
    {
        if (!string.Equals(mediaType, "video", StringComparison.OrdinalIgnoreCase))
        {
            return "已生成";
        }

        return !string.IsNullOrWhiteSpace(output.FilePath) && File.Exists(output.FilePath)
            ? "视频已保存到本地，可在线播放"
            : "视频可在线播放；如需本地文件请手动另存为";
    }

    private static string? ResolvePreviewBase64(GenerationTaskOutputRecord output)
    {
        if (!string.IsNullOrWhiteSpace(output.ImageBase64))
        {
            return output.ImageBase64;
        }

        if (!string.IsNullOrWhiteSpace(output.FilePath) && File.Exists(output.FilePath))
        {
            return Convert.ToBase64String(File.ReadAllBytes(output.FilePath));
        }

        return null;
    }
}

public sealed class HistoryTaskItemViewModel
{
    public HistoryTaskItemViewModel(GenerationTaskHistoryRecord record)
    {
        Id = record.Id;
        Prompt = record.Prompt;
        Status = record.Status;
        Error = record.Error;
        OutputCount = record.OutputCount;
        MediaType = string.IsNullOrWhiteSpace(record.PreviewMediaType) ? (string.Equals(record.Mode, "video", StringComparison.OrdinalIgnoreCase) ? "video" : "image") : record.PreviewMediaType;
        CreatedAtText = FormatTime(record.CreatedAt);
        Model = TryReadModel(record.ParametersJson);
    }

    public string Id { get; }

    public string Prompt { get; }

    public string Status { get; }

    public string? Error { get; }

    public int OutputCount { get; }

    public string MediaType { get; }

    public string CreatedAtText { get; }

    public string Model { get; }

    public string ShortId => Id.Length <= 8 ? Id : Id[..8];

    public string Summary => $"{CreatedAtText} · {Status} · {OutputCount} {(string.Equals(MediaType, "video", StringComparison.OrdinalIgnoreCase) ? "个视频" : "张图片")}";

    private static string FormatTime(string value)
    {
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed.ToLocalTime().ToString("MM-dd HH:mm", CultureInfo.CurrentCulture)
            : value;
    }

    private static string? TryGetStringProperty(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static string TryReadModel(string parametersJson)
    {
        if (string.IsNullOrWhiteSpace(parametersJson))
        {
            return "";
        }

        try
        {
            using var document = JsonDocument.Parse(parametersJson);
            return TryGetStringProperty(document.RootElement, "model")
                   ?? TryGetStringProperty(document.RootElement, "Model")
                   ?? "";
        }
        catch (JsonException)
        {
            return "";
        }
    }
}
