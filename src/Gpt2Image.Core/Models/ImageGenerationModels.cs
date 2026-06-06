namespace Gpt2Image.Core.Models;

public sealed class ImageGenerationRequest
{
    public string Prompt { get; init; } = "";
    public IReadOnlyList<ImageInputAsset> Images { get; init; } = Array.Empty<ImageInputAsset>();
    public ImageInputAsset? Mask { get; init; }
    public string Mode { get; init; } = "generate";
    public string? Model { get; init; }
    public string Size { get; init; } = "1024x1024";
    public string Quality { get; init; } = "auto";
    public string ResponseFormat { get; init; } = "b64_json";
    public string OutputFormat { get; init; } = "png";
    public int? OutputCompression { get; init; }
    public string Background { get; init; } = "auto";
    public int Count { get; init; } = 1;
    public bool Stream { get; init; }
}

public sealed class ImageInputAsset
{
    public string FilePath { get; init; } = "";
    public string MimeType { get; init; } = "";
}

public sealed class GenerationResult
{
    public string GenerationId { get; init; } = Guid.NewGuid().ToString("N");
    public string Status { get; init; } = "completed";
    public IReadOnlyList<GeneratedImageOutput> Images { get; init; } = Array.Empty<GeneratedImageOutput>();
    public string? RevisedPrompt { get; init; }
    public TokenUsage? Usage { get; init; }
    public string? Error { get; init; }
}

public sealed class GeneratedImageOutput
{
    public string? FilePath { get; init; }
    public string? Base64 { get; init; }
    public string? Url { get; init; }
    public int Index { get; init; }
    public string? Size { get; init; }
    public string? RevisedPrompt { get; init; }
    public string OutputRole { get; init; } = "final";
}

public sealed class TokenUsage
{
    public int? InputTokens { get; init; }
    public int? OutputTokens { get; init; }
    public int? TotalTokens { get; init; }
}

public sealed class AgentRunRequest
{
    public string Goal { get; init; } = "";
    public IReadOnlyList<ImageInputAsset> ReferenceImages { get; init; } = Array.Empty<ImageInputAsset>();
    public int MaxRounds { get; init; } = 5;
    public bool UseWebSearch { get; init; } = true;
    public string? MainlineModel { get; init; }
    public string? ImageModel { get; init; }
}

public enum ImageStreamEventKind
{
    PartialImage,
    FinalImage,
    TextDelta,
    Completed,
    Error
}

public sealed class ImageStreamEvent
{
    public ImageStreamEventKind Kind { get; init; }
    public string? Base64 { get; init; }
    public string? Text { get; init; }
    public int? Index { get; init; }
    public int? PartialImageIndex { get; init; }
    public string? ResponseId { get; init; }
    public string? Error { get; init; }
}
