using Gpt2Image.Wpf.ViewModels;

namespace Gpt2Image.Wpf;

public partial class MainWindow
{
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
