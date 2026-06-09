namespace Gpt2Image.Core.Models;

public sealed class VideoGenerationRequest
{
    public string Prompt { get; init; } = "";
    public string? Model { get; init; }
    public int? Duration { get; init; }
    public string AspectRatio { get; init; } = "16:9";
    public string Resolution { get; init; } = "720p";
    public VideoInputReference? Image { get; init; }
    public IReadOnlyList<VideoInputReference> ReferenceImages { get; init; } = Array.Empty<VideoInputReference>();
}

public sealed class VideoInputReference
{
    public string Url { get; init; } = "";
}

public sealed class VideoGenerationResult
{
    public string GenerationId { get; init; } = Guid.NewGuid().ToString("N");
    public string Status { get; init; } = "completed";
    public IReadOnlyList<GeneratedVideoOutput> Videos { get; init; } = Array.Empty<GeneratedVideoOutput>();
    public TokenUsage? Usage { get; init; }
    public string? Error { get; init; }
    public string? ProviderRequestId { get; init; }
    public int? Progress { get; init; }
}

public sealed class GeneratedVideoOutput
{
    public int Index { get; init; }
    public string? FilePath { get; init; }
    public string? Url { get; init; }
    public string? MimeType { get; init; }
    public double? DurationSeconds { get; init; }
    public string? Sha256 { get; init; }
    public string OutputRole { get; init; } = "final";
}
