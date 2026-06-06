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

    public string Title { get; }

    public Action<string>? SavedFilePathChanged { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasImage))]
    [NotifyPropertyChangedFor(nameof(PreviewImageSource))]
    private string? _previewBase64;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasImage))]
    [NotifyPropertyChangedFor(nameof(PreviewImageSource))]
    private string? _sourceUrl;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSaved))]
    [NotifyPropertyChangedFor(nameof(FileName))]
    private string? _filePath;

    [ObservableProperty]
    private string _status = "等待";

    [ObservableProperty]
    private bool _isLoading = true;

    [ObservableProperty]
    private bool _hasError;

    public string? PreviewImageSource => !string.IsNullOrWhiteSpace(PreviewBase64) ? PreviewBase64 : SourceUrl;

    public bool HasImage => !string.IsNullOrWhiteSpace(PreviewBase64) || !string.IsNullOrWhiteSpace(SourceUrl);

    public bool IsSaved => !string.IsNullOrWhiteSpace(FilePath);

    public string FileName => string.IsNullOrWhiteSpace(FilePath) ? $"{Title}.png" : Path.GetFileName(FilePath);

    [RelayCommand(CanExecute = nameof(CanSaveImage))]
    private async Task SaveAsAsync(CancellationToken cancellationToken)
    {
        if (!HasImage)
        {
            Status = "没有可保存的图片";
            return;
        }

        var extension = string.IsNullOrWhiteSpace(FilePath) ? ".png" : Path.GetExtension(FilePath);
        var dialog = new SaveFileDialog
        {
            FileName = FileName,
            DefaultExt = extension,
            Filter = $"{extension.TrimStart('.').ToUpperInvariant()} 图片|*{extension}|所有文件|*.*"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var bytes = await LoadImageBytesAsync(cancellationToken);
            if (bytes.Length == 0)
            {
                Status = "没有可保存的图片";
                return;
            }

            await File.WriteAllBytesAsync(dialog.FileName, bytes, cancellationToken);
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

    private bool CanSaveImage() => HasImage;

    private bool CanUseSavedFile() => !string.IsNullOrWhiteSpace(FilePath) && File.Exists(FilePath);

    private static byte[] DecodeBase64Image(string base64)
    {
        var commaIndex = base64.IndexOf(',');
        if (commaIndex >= 0)
        {
            base64 = base64[(commaIndex + 1)..];
        }

        return Convert.FromBase64String(base64);
    }

    private async Task<byte[]> LoadImageBytesAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(PreviewBase64))
        {
            return DecodeBase64Image(PreviewBase64);
        }

        if (string.IsNullOrWhiteSpace(SourceUrl))
        {
            return Array.Empty<byte>();
        }

        Status = "正在下载图片";
        using var response = await ImageDownloadClient.GetAsync(SourceUrl, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
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
    }

    partial void OnPreviewBase64Changed(string? value)
    {
        SaveAsCommand.NotifyCanExecuteChanged();
    }

    partial void OnSourceUrlChanged(string? value)
    {
        SaveAsCommand.NotifyCanExecuteChanged();
    }
}
