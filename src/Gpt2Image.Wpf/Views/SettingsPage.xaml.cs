using Gpt2Image.Wpf.ViewModels;

namespace Gpt2Image.Wpf.Views;

public partial class SettingsPage
{
    private bool _isSyncingPassword;

    public SettingsPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnPasswordChanged(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_isSyncingPassword)
        {
            return;
        }

        if (DataContext is SettingsPageViewModel viewModel)
        {
            viewModel.ApiKey = ApiKeyBox.Password;
        }
    }

    private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        SyncPasswordFromViewModel();
    }

    private void OnDataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        SyncPasswordFromViewModel();
    }

    private void SyncPasswordFromViewModel()
    {
        if (DataContext is not SettingsPageViewModel viewModel)
        {
            return;
        }

        if (ApiKeyBox.Password == viewModel.ApiKey)
        {
            return;
        }

        try
        {
            _isSyncingPassword = true;
            ApiKeyBox.Password = viewModel.ApiKey;
        }
        finally
        {
            _isSyncingPassword = false;
        }
    }
}
