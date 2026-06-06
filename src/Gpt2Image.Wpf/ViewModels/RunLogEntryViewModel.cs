namespace Gpt2Image.Wpf.ViewModels;

public sealed class RunLogEntryViewModel
{
    public RunLogEntryViewModel(DateTimeOffset timestamp, string level, string message, string? detail = null)
    {
        Timestamp = timestamp;
        Level = level;
        Message = message;
        Detail = detail;
    }

    public DateTimeOffset Timestamp { get; }

    public string Level { get; }

    public string Message { get; }

    public string? Detail { get; }

    public string TimeText => Timestamp.ToLocalTime().ToString("HH:mm:ss");
}
