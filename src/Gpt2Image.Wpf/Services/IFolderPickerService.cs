namespace Gpt2Image.Wpf.Services;

public interface IFolderPickerService
{
    string? PickFolder(string? initialDirectory = null);
}
