using System.IO;
using System.Windows;
using System.Windows.Controls;
using LibVLCSharp.Shared;

namespace Gpt2Image.Wpf.Controls;

public partial class VideoPlayerControl : System.Windows.Controls.UserControl, IDisposable
{
    private static readonly Lazy<LibVLC> SharedLibVlc = new(() =>
    {
        LibVLCSharp.Shared.Core.Initialize();
        return new LibVLC();
    });

    private MediaPlayer? _mediaPlayer;
    private Media? _currentMedia;
    private bool _disposed;

    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source),
        typeof(string),
        typeof(VideoPlayerControl),
        new PropertyMetadata("", OnSourceChanged));

    public static readonly DependencyProperty StatusTextProperty = DependencyProperty.Register(
        nameof(StatusText),
        typeof(string),
        typeof(VideoPlayerControl),
        new PropertyMetadata("等待播放"));

    public VideoPlayerControl()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        EnsureMediaPlayer();
    }

    public string Source
    {
        get => (string)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public string StatusText
    {
        get => (string)GetValue(StatusTextProperty);
        set => SetValue(StatusTextProperty, value);
    }

    private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is VideoPlayerControl control)
        {
            control.ResetForSource();
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        EnsureMediaPlayer();
        StatusText = string.IsNullOrWhiteSpace(Source) ? "没有视频" : "等待播放";
    }

    private void OnPlayClicked(object sender, RoutedEventArgs e)
    {
        PlaySource();
    }

    private void OnPauseClicked(object sender, RoutedEventArgs e)
    {
        if (_mediaPlayer?.IsPlaying == true)
        {
            _mediaPlayer.Pause();
            StatusText = "已暂停";
        }
    }

    private void OnStopClicked(object sender, RoutedEventArgs e)
    {
        _mediaPlayer?.Stop();
        StatusText = "已停止";
    }

    private void PlaySource()
    {
        if (string.IsNullOrWhiteSpace(Source))
        {
            StatusText = "没有可播放的视频地址";
            return;
        }

        try
        {
            var player = EnsureMediaPlayer();
            _currentMedia?.Dispose();
            _currentMedia = CreateMedia(Source.Trim());
            PlaceholderText.Visibility = Visibility.Collapsed;
            player.Play(_currentMedia);
            StatusText = "正在播放";
        }
        catch (Exception ex)
        {
            StatusText = $"播放失败：{ex.Message}";
            PlaceholderText.Visibility = Visibility.Visible;
        }
    }

    private MediaPlayer EnsureMediaPlayer()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(VideoPlayerControl));
        }

        if (_mediaPlayer is not null)
        {
            return _mediaPlayer;
        }

        _mediaPlayer = new MediaPlayer(SharedLibVlc.Value);
        VideoView.MediaPlayer = _mediaPlayer;
        return _mediaPlayer;
    }

    private static Media CreateMedia(string source)
    {
        if (File.Exists(source))
        {
            return new Media(SharedLibVlc.Value, source, FromType.FromPath);
        }

        if (Uri.TryCreate(source, UriKind.Absolute, out var uri))
        {
            return new Media(SharedLibVlc.Value, uri);
        }

        return new Media(SharedLibVlc.Value, source, FromType.FromLocation);
    }

    private void ResetForSource()
    {
        if (_disposed)
        {
            return;
        }

        _mediaPlayer?.Stop();
        _currentMedia?.Dispose();
        _currentMedia = null;
        PlaceholderText.Visibility = Visibility.Visible;
        StatusText = string.IsNullOrWhiteSpace(Source) ? "没有视频" : "等待播放";
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        DisposeMediaPlayer();
    }

    private void DisposeMediaPlayer()
    {
        _mediaPlayer?.Stop();
        VideoView.MediaPlayer = null;
        _currentMedia?.Dispose();
        _currentMedia = null;
        _mediaPlayer?.Dispose();
        _mediaPlayer = null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Loaded -= OnLoaded;
        Unloaded -= OnUnloaded;
        DisposeMediaPlayer();
    }
}
