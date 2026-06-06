using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Gpt2Image.Wpf.ViewModels;

public sealed partial class GenerationPreviewItemViewModel : ObservableObject
{
    public GenerationPreviewItemViewModel(int index)
    {
        Index = index;
        Title = $"图片 {index + 1}";
    }

    public int Index { get; }

    public string Title { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasImage))]
    private string? _previewBase64;

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

    public bool HasImage => !string.IsNullOrWhiteSpace(PreviewBase64);

    public bool IsSaved => !string.IsNullOrWhiteSpace(FilePath);

    public string FileName => string.IsNullOrWhiteSpace(FilePath) ? $"{Title}.png" : Path.GetFileName(FilePath);
}
