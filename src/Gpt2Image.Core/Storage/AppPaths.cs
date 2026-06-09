namespace Gpt2Image.Core.Storage;

public sealed class AppPaths
{
    private AppPaths(string root, string? logsDirectory = null)
    {
        Root = root;
        DataDirectory = Path.Combine(root, "data");
        DatabasePath = Path.Combine(DataDirectory, "app.db");
        ImagesDirectory = Path.Combine(root, "images");
        VideosDirectory = Path.Combine(root, "videos");
        ChatAttachmentsDirectory = Path.Combine(root, "chat-attachments");
        PartialImagesDirectory = Path.Combine(root, "cache", "partial");
        LogsDirectory = logsDirectory ?? Path.Combine(root, "logs");
    }

    public string Root { get; }
    public string DataDirectory { get; }
    public string DatabasePath { get; }
    public string ImagesDirectory { get; }
    public string VideosDirectory { get; }
    public string ChatAttachmentsDirectory { get; }
    public string PartialImagesDirectory { get; }
    public string LogsDirectory { get; }

    public static AppPaths CreateDefault()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programLogsDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
        return CreateForRoot(Path.Combine(localAppData, "Gpt2ImageWpf"), programLogsDirectory);
    }

    public static AppPaths CreateForRoot(string root, string? logsDirectory = null) => new(root, logsDirectory);

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(ImagesDirectory);
        Directory.CreateDirectory(VideosDirectory);
        Directory.CreateDirectory(ChatAttachmentsDirectory);
        Directory.CreateDirectory(PartialImagesDirectory);
        Directory.CreateDirectory(LogsDirectory);
    }
}
