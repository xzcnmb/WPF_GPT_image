namespace Gpt2Image.Core.Models;

public sealed class BackendProfile
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string BaseUrl { get; init; } = "";
    public string ApiKey { get; init; } = "";
    public string Protocol { get; init; } = BackendProtocol.OpenAiImages;
    public string ProviderKind { get; init; } = BackendProviderKind.Custom;
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
    public const string Coding = "coding";

    public static IReadOnlyList<string> KnownValues { get; } = new[]
    {
        Prompt,
        Chat,
        Image,
        Video,
        Agent,
        Coding
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
        Coding => "编码工作台",
        _ => "图片生成 API"
    };

    public static string DefaultProfileId(string? role) => Normalize(role) switch
    {
        Prompt => "prompt-default",
        Chat => "chat-default",
        Video => "video-default",
        Agent => "agent-default",
        Coding => "coding-default",
        _ => "image-default"
    };
}

public static class BackendProviderKind
{
    public const string Custom = "custom";
    public const string OpenAi = "openai";
    public const string DeepSeek = "deepseek";
    public const string MiniMax = "minimax";
    public const string Mimo = "mimo";
    public const string Mino = "mino";
    public const string Kimi = "kimi";
    public const string Qwen = "qwen";
    public const string Glm = "glm";
    public const string Routin = "routin";

    public static IReadOnlyList<string> KnownValues { get; } = new[]
    {
        Custom,
        OpenAi,
        DeepSeek,
        MiniMax,
        Mimo,
        Mino,
        Kimi,
        Qwen,
        Glm,
        Routin
    };

    public static string Normalize(string? value)
    {
        var trimmed = value?.Trim();
        return KnownValues.Contains(trimmed, StringComparer.OrdinalIgnoreCase)
            ? KnownValues.First(item => string.Equals(item, trimmed, StringComparison.OrdinalIgnoreCase))
            : Custom;
    }

    public static string DisplayName(string? value) => Normalize(value) switch
    {
        OpenAi => "OpenAI",
        DeepSeek => "DeepSeek 深度求索",
        MiniMax => "MiniMax",
        Mimo => "MiMo / Xiaomi",
        Mino => "Mino / 旧自定义兼容",
        Kimi => "Kimi / Moonshot",
        Qwen => "Qwen / 通义千问",
        Glm => "GLM / 智谱清言",
        Routin => "Routin",
        _ => "自定义 OpenAI-compatible"
    };
}

public sealed class BackendProviderPreset
{
    public BackendProviderPreset(string kind, string label, string baseUrl, string mainlineModel, string description)
    {
        Kind = kind;
        Label = label;
        BaseUrl = baseUrl;
        MainlineModel = mainlineModel;
        Description = description;
    }

    public string Kind { get; }
    public string Label { get; }
    public string BaseUrl { get; }
    public string MainlineModel { get; }
    public string Description { get; }
}

public static class BackendProviderPresetCatalog
{
    public static IReadOnlyList<BackendProviderPreset> TextCompatiblePresets { get; } = new[]
    {
        new BackendProviderPreset(BackendProviderKind.Custom, "自定义", "https://api.example.com/v1", "", "填写任意 OpenAI-compatible /v1/chat/completions 接口。"),
        new BackendProviderPreset(BackendProviderKind.OpenAi, "OpenAI", "https://api.openai.com/v1", "gpt-4o-mini", "OpenAI 官方兼容接口，适合通用对话、提示词润色和编码。"),
        new BackendProviderPreset(BackendProviderKind.DeepSeek, "DeepSeek", "https://api.deepseek.com/v1", "deepseek-chat", "DeepSeek 官方 OpenAI-compatible 对话接口，可用于日常对话和编码规划。"),
        new BackendProviderPreset(BackendProviderKind.MiniMax, "MiniMax", "https://api.minimax.chat/v1", "MiniMax-Text-01", "MiniMax OpenAI-compatible 对话接口；Starter key 已验证可用 MiniMax-Text-01。"),
        new BackendProviderPreset(BackendProviderKind.Mimo, "MiMo", "https://token-plan-cn.xiaomimimo.com/v1", "mimo-v2.5-pro", "小米 MiMo Token Plan OpenAI-compatible 对话接口；tp- 开头密钥优先使用该地址。"),
        new BackendProviderPreset(BackendProviderKind.Mino, "Mino", "https://api.mino.ai/v1", "mino-chat", "Mino 或同名中转接口预设；如供应商地址不同，可在应用后手动修改。"),
        new BackendProviderPreset(BackendProviderKind.Kimi, "Kimi", "https://api.moonshot.cn/v1", "moonshot-v1-8k", "Moonshot/Kimi OpenAI-compatible 接口，适合中文日常对话与代码问答。"),
        new BackendProviderPreset(BackendProviderKind.Qwen, "Qwen", "https://dashscope.aliyuncs.com/compatible-mode/v1", "qwen-plus", "阿里云 DashScope OpenAI 兼容模式，适合通义千问系列模型。"),
        new BackendProviderPreset(BackendProviderKind.Glm, "GLM", "https://open.bigmodel.cn/api/paas/v4", "glm-4-flash", "智谱 GLM OpenAI-compatible 接口，适合日常对话和轻量编码。")
    };

    public static BackendProviderPreset GetTextPreset(string? kind)
    {
        var normalized = BackendProviderKind.Normalize(kind);
        return TextCompatiblePresets.FirstOrDefault(item => item.Kind == normalized) ?? TextCompatiblePresets[0];
    }
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
        ChatCompletionsImageJson => "OpenAI-compatible Chat Completions（DeepSeek/Kimi/Qwen/GLM 等）",
        RoutinXaiVideo => "Routin xAI Video /xai/v1/videos/generations",
        _ => "OpenAI Images /v1/images/generations"
    };

    public static string Description(string? value) => Normalize(value) switch
    {
        OpenAiResponses => "适合支持 Responses image_generation 工具的接口。",
        ChatCompletionsImageJson => "适合 DeepSeek、MiniMax、MiMo、Kimi、Qwen、GLM 等 OpenAI-compatible /v1/chat/completions 接口；也兼容把图片以 Markdown、data URL 或 JSON 放在聊天回复里的中转接口。",
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
