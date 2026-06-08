using System.IO;
using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace Gpt2Image.Wpf.ViewModels;

public sealed partial class GenerationPreviewItemViewModel : ObservableObject
{
    private static readonly HttpClient ImageDownloadClient = new()
    {
        Timeout = TimeSpan.FromMinutes(5)
    };

    public GenerationPreviewItemViewModel(int index)
    {
        Index = index;
        Title = $"图片 {index + 1}";
    }

    public int Index { get; }

    public string Title { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsImage))]
    [NotifyPropertyChangedFor(nameof(IsVideo))]
    [NotifyPropertyChangedFor(nameof(HasImage))]
    [NotifyPropertyChangedFor(nameof(HasVideo))]
    [NotifyPropertyChangedFor(nameof(PlaybackSource))]
    [NotifyPropertyChangedFor(nameof(FileName))]
    private string _mediaType = "image";

    public Action<string>? SavedFilePathChanged { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasImage))]
    [NotifyPropertyChangedFor(nameof(PreviewImageSource))]
    private string? _previewBase64;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasImage))]
    [NotifyPropertyChangedFor(nameof(HasVideo))]
    [NotifyPropertyChangedFor(nameof(HasSourceUrl))]
    [NotifyPropertyChangedFor(nameof(PlaybackSource))]
    [NotifyPropertyChangedFor(nameof(PreviewImageSource))]
    private string? _sourceUrl;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSaved))]
    [NotifyPropertyChangedFor(nameof(HasVideo))]
    [NotifyPropertyChangedFor(nameof(PlaybackSource))]
    [NotifyPropertyChangedFor(nameof(FileName))]
    private string? _filePath;

    [ObservableProperty]
    private string _status = "等待";

    [ObservableProperty]
    private bool _isLoading = true;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DurationText))]
    private double? _durationSeconds;

    public string? PreviewImageSource => IsImage && !string.IsNullOrWhiteSpace(PreviewBase64) ? PreviewBase64 : SourceUrl;

    public bool IsImage => string.Equals(MediaType, "image", StringComparison.OrdinalIgnoreCase);

    public bool IsVideo => string.Equals(MediaType, "video", StringComparison.OrdinalIgnoreCase);

    public bool HasImage => IsImage && (!string.IsNullOrWhiteSpace(PreviewBase64) || !string.IsNullOrWhiteSpace(SourceUrl));

    public bool HasVideo => IsVideo && (!string.IsNullOrWhiteSpace(FilePath) || !string.IsNullOrWhiteSpace(SourceUrl));

    public bool IsSaved => !string.IsNullOrWhiteSpace(FilePath);

    public bool HasSourceUrl => !string.IsNullOrWhiteSpace(SourceUrl);

    public string? PlaybackSource => !string.IsNullOrWhiteSpace(FilePath) && File.Exists(FilePath) ? FilePath : SourceUrl;

    public string FileName => string.IsNullOrWhiteSpace(FilePath) ? $"{Title}.{(IsVideo ? "mp4" : "png")}" : Path.GetFileName(FilePath);

    public string DurationText => DurationSeconds is > 0 ? $"{DurationSeconds:0.#} 秒" : "";

    [RelayCommand(CanExecute = nameof(CanSaveImage))]
    private async Task SaveAsAsync(CancellationToken cancellationToken)
    {
        if (!CanSaveMedia())
        {
            Status = IsVideo ? "没有可保存的视频" : "没有可保存的图片";
            return;
        }

        var extension = string.IsNullOrWhiteSpace(FilePath) ? (IsVideo ? ".mp4" : ".png") : Path.GetExtension(FilePath);
        var mediaLabel = IsVideo ? "视频" : "图片";
        var dialog = new SaveFileDialog
        {
            FileName = FileName,
            DefaultExt = extension,
            Filter = $"{extension.TrimStart('.').ToUpperInvariant()} {mediaLabel}|*{extension}|所有文件|*.*"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var saved = await SaveMediaToFileAsync(dialog.FileName, cancellationToken);
            if (!saved)
            {
                Status = IsVideo ? "没有可保存的视频" : "没有可保存的图片";
                return;
            }
            FilePath = dialog.FileName;
            try
            {
                SavedFilePathChanged?.Invoke(dialog.FileName);
                Status = $"已另存为 {Path.GetFileName(dialog.FileName)}";
            }
            catch (Exception ex)
            {
                Status = $"已另存为 {Path.GetFileName(dialog.FileName)}，历史路径更新失败：{ex.Message}";
            }
        }
        catch (Exception ex)
        {
            Status = $"另存失败：{ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseSavedFile))]
    private void OpenFile()
    {
        OpenShellTarget(FilePath);
    }

    [RelayCommand(CanExecute = nameof(CanUseSavedFile))]
    private void OpenFolder()
    {
        if (string.IsNullOrWhiteSpace(FilePath))
        {
            Status = "文件不存在";
            return;
        }

        OpenShellTarget(Path.GetDirectoryName(FilePath));
    }

    [RelayCommand(CanExecute = nameof(CanUseSavedFile))]
    private void CopyPath()
    {
        if (string.IsNullOrWhiteSpace(FilePath) || !File.Exists(FilePath))
        {
            Status = "文件不存在";
            return;
        }

        Clipboard.SetText(FilePath);
        Status = "路径已复制";
    }

    [RelayCommand(CanExecute = nameof(CanUseSourceUrl))]
    private void CopySourceUrl()
    {
        if (string.IsNullOrWhiteSpace(SourceUrl))
        {
            Status = "视频链接为空";
            return;
        }

        Clipboard.SetText(SourceUrl);
        Status = "链接已复制";
    }

    public void SetTitle(string title)
    {
        Title = title;
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(FileName));
    }

    private bool CanSaveImage() => CanSaveMedia();

    private bool CanSaveMedia()
    {
        if (!string.IsNullOrWhiteSpace(FilePath) && File.Exists(FilePath))
        {
            return true;
        }

        return IsVideo
            ? !string.IsNullOrWhiteSpace(SourceUrl)
            : HasImage;
    }

    private bool CanUseSavedFile() => !string.IsNullOrWhiteSpace(FilePath) && File.Exists(FilePath);

    private bool CanUseSourceUrl() => !string.IsNullOrWhiteSpace(SourceUrl);

    private static byte[] DecodeBase64Image(string base64)
    {
        var commaIndex = base64.IndexOf(',');
        if (commaIndex >= 0)
        {
            base64 = base64[(commaIndex + 1)..];
        }

        return Convert.FromBase64String(base64);
    }

    private async Task<bool> SaveMediaToFileAsync(string targetPath, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(FilePath) && File.Exists(FilePath))
        {
            await using var source = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
            await using var target = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.Read, 81920, useAsync: true);
            await source.CopyToAsync(target, 81920, cancellationToken);
            return true;
        }

        if (IsImage && !string.IsNullOrWhiteSpace(PreviewBase64))
        {
            var bytes = DecodeBase64Image(PreviewBase64);
            await File.WriteAllBytesAsync(targetPath, bytes, cancellationToken);
            return bytes.Length > 0;
        }

        if (string.IsNullOrWhiteSpace(SourceUrl))
        {
            return false;
        }

        Status = IsVideo ? "正在下载视频" : "正在下载图片";
        using var response = await ImageDownloadClient.GetAsync(SourceUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var remote = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.Read, 81920, useAsync: true);
        await remote.CopyToAsync(output, 81920, cancellationToken);
        return true;
    }

    private void OpenShellTarget(string? target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            Status = "文件不存在";
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Status = $"打开失败：{ex.Message}";
        }
    }

    partial void OnFilePathChanged(string? value)
    {
        SaveAsCommand.NotifyCanExecuteChanged();
        OpenFileCommand.NotifyCanExecuteChanged();
        OpenFolderCommand.NotifyCanExecuteChanged();
        CopyPathCommand.NotifyCanExecuteChanged();
        CopySourceUrlCommand.NotifyCanExecuteChanged();
    }

    partial void OnPreviewBase64Changed(string? value)
    {
        SaveAsCommand.NotifyCanExecuteChanged();
    }

    partial void OnSourceUrlChanged(string? value)
    {
        SaveAsCommand.NotifyCanExecuteChanged();
        CopySourceUrlCommand.NotifyCanExecuteChanged();
    }

    partial void OnMediaTypeChanged(string value)
    {
        SaveAsCommand.NotifyCanExecuteChanged();
    }
}
