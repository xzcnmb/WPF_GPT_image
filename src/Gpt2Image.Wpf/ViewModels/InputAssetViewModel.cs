using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using Gpt2Image.Core.Models;

namespace Gpt2Image.Wpf.ViewModels;

public sealed partial class InputAssetViewModel : ObservableObject
{
    public string FilePath { get; init; } = "";
    public string FileName => Path.GetFileName(FilePath);
    public string MimeType { get; init; } = "";
    public string Sha256 { get; init; } = "";
    public long ByteLength { get; init; }
    public string SizeText => ByteLength <= 0 ? "" : $"{ByteLength / 1024d / 1024d:0.##} MB";
    public string PreviewSource => FilePath;

    public ImageInputAsset ToModel() => new()
    {
        FilePath = FilePath,
        MimeType = MimeType
    };
}
