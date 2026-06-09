using System.IO;
using System.Windows.Forms;

namespace Gpt2Image.Wpf.Services;

public sealed class FolderPickerService : IFolderPickerService
{
    public string? PickFolder(string? initialDirectory = null)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择 AI 自动编码工作区",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(initialDirectory) ? initialDirectory : ""
        };

        return dialog.ShowDialog() == DialogResult.OK
            ? dialog.SelectedPath
            : null;
    }
}
