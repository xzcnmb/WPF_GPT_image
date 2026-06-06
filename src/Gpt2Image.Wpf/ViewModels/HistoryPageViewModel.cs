using CommunityToolkit.Mvvm.ComponentModel;

namespace Gpt2Image.Wpf.ViewModels;

public sealed partial class HistoryPageViewModel : ObservableObject
{
    [ObservableProperty]
    private string _status = "";
}
