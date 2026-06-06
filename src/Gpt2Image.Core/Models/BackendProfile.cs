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
    public int Concurrency { get; init; } = 1;
    public int Priority { get; init; }
    public bool IsEnabled { get; init; } = true;
    public DateTimeOffset? FailureCooldownUntil { get; init; }
}

public static class BackendProtocol
{
    public const string OpenAiImages = "openai-images";
    public const string OpenAiResponses = "openai-responses";
    public const string ChatCompletionsImageJson = "chat-completions-image-json";

    public static IReadOnlyList<string> KnownValues { get; } = new[]
    {
        OpenAiImages,
        OpenAiResponses,
        ChatCompletionsImageJson
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
        _ => "OpenAI Images /v1/images/generations"
    };

    public static string Description(string? value) => Normalize(value) switch
    {
        OpenAiResponses => "适合支持 Responses image_generation 工具的接口。",
        ChatCompletionsImageJson => "适合把图片以 Markdown、data URL 或 JSON 放在聊天回复里的中转接口。",
        _ => "适合标准 OpenAI 兼容图片接口，默认推荐。"
    };

    public static string NormalizeBaseUrl(string baseUrl, string? protocol)
    {
        var trimmed = (baseUrl ?? string.Empty).Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return string.Empty;
        }

        _ = Normalize(protocol);
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
