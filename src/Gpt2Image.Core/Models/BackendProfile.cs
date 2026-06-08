namespace Gpt2Image.Core.Models;

public sealed class BackendProfile
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string BaseUrl { get; init; } = "";
    public string ApiKey { get; init; } = "";
    public string Protocol { get; init; } = BackendProtocol.OpenAiImages;
    public string MainlineModel { get; init; } = "";
    public string ImageModel { get; init; } = "";
    public string VideoModel { get; init; } = "";
    public int Concurrency { get; init; } = 1;
    public int Priority { get; init; }
    public bool IsEnabled { get; init; } = true;
    public bool SupportsPromptOptimization { get; init; } = true;
    public bool SupportsChat { get; init; } = true;
    public bool SupportsImageGeneration { get; init; } = true;
    public bool SupportsVideoGeneration { get; init; }
    public bool SupportsAgent { get; init; }
    public DateTimeOffset? FailureCooldownUntil { get; init; }
}

public static class BackendProfileRole
{
    public const string Prompt = "prompt";
    public const string Chat = "chat";
    public const string Image = "image";
    public const string Video = "video";
    public const string Agent = "agent";

    public static IReadOnlyList<string> KnownValues { get; } = new[]
    {
        Prompt,
        Chat,
        Image,
        Video,
        Agent
    };

    public static string Normalize(string? value)
    {
        var trimmed = value?.Trim();
        return KnownValues.Contains(trimmed, StringComparer.OrdinalIgnoreCase)
            ? KnownValues.First(item => string.Equals(item, trimmed, StringComparison.OrdinalIgnoreCase))
            : Image;
    }

    public static string DisplayName(string? value) => Normalize(value) switch
    {
        Prompt => "提示词润色 / 对话 API",
        Chat => "聊天 API",
        Video => "视频生成 API",
        Agent => "Agent / Responses API",
        _ => "图片生成 API"
    };

    public static string DefaultProfileId(string? role) => Normalize(role) switch
    {
        Prompt => "prompt-default",
        Chat => "chat-default",
        Video => "video-default",
        Agent => "agent-default",
        _ => "image-default"
    };
}

public static class BackendProtocol
{
    public const string OpenAiImages = "openai-images";
    public const string OpenAiResponses = "openai-responses";
    public const string ChatCompletionsImageJson = "chat-completions-image-json";
    public const string RoutinXaiVideo = "routin-xai-video";

    public static IReadOnlyList<string> KnownValues { get; } = new[]
    {
        OpenAiImages,
        OpenAiResponses,
        ChatCompletionsImageJson,
        RoutinXaiVideo
    };

    public static string Normalize(string? value)
    {
        var trimmed = value?.Trim();
        return KnownValues.Contains(trimmed, StringComparer.OrdinalIgnoreCase)
            ? KnownValues.First(item => string.Equals(item, trimmed, StringComparison.OrdinalIgnoreCase))
            : OpenAiImages;
    }

    public static string DisplayName(string? value) => Normalize(value) switch
    {
        OpenAiResponses => "OpenAI Responses /v1/responses",
        ChatCompletionsImageJson => "Chat Completions 图片 /v1/chat/completions",
        RoutinXaiVideo => "Routin xAI Video /xai/v1/videos/generations",
        _ => "OpenAI Images /v1/images/generations"
    };

    public static string Description(string? value) => Normalize(value) switch
    {
        OpenAiResponses => "适合支持 Responses image_generation 工具的接口。",
        ChatCompletionsImageJson => "适合把图片以 Markdown、data URL 或 JSON 放在聊天回复里的中转接口。",
        RoutinXaiVideo => "适合 Routin xAI 视频中转接口，提交 request_id 后轮询视频结果。",
        _ => "适合标准 OpenAI 兼容图片接口，默认推荐。"
    };

    public static bool SupportsVideo(string? value) => Normalize(value) == RoutinXaiVideo;

    public static bool SupportsAgent(string? value) => Normalize(value) == OpenAiResponses;

    public static bool SupportsImage(string? value) => Normalize(value) is OpenAiImages or OpenAiResponses or ChatCompletionsImageJson;

    public static bool SupportsChat(string? value) => Normalize(value) != RoutinXaiVideo;

    public static string NormalizeBaseUrl(string baseUrl, string? protocol)
    {
        var trimmed = (baseUrl ?? string.Empty).Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return string.Empty;
        }

        var normalizedProtocol = Normalize(protocol);
        if (string.Equals(normalizedProtocol, RoutinXaiVideo, StringComparison.OrdinalIgnoreCase))
        {
            var routinIndex = trimmed.IndexOf("/xai/v1/", StringComparison.OrdinalIgnoreCase);
            if (routinIndex >= 0)
            {
                return trimmed[..(routinIndex + 7)];
            }

            return trimmed.EndsWith("/xai/v1", StringComparison.OrdinalIgnoreCase)
                ? trimmed
                : trimmed;
        }

        var v1Index = trimmed.IndexOf("/v1/", StringComparison.OrdinalIgnoreCase);
        if (v1Index >= 0)
        {
            return trimmed[..(v1Index + 3)];
        }

        return trimmed.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : $"{trimmed}/v1";
    }
}
