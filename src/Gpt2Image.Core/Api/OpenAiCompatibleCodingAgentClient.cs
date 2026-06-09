using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Gpt2Image.Core.Models;

namespace Gpt2Image.Core.Api;

public sealed class OpenAiCompatibleCodingAgentClient : ICodingAgentClient
{
    private const string DefaultCodingModel = "gpt-4o-mini";
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(3);
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;

    public OpenAiCompatibleCodingAgentClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _httpClient.Timeout = Timeout.InfiniteTimeSpan;
    }

    public async Task<CodingAgentResponse> GenerateProposalAsync(
        BackendProfile profile,
        CodingAgentRequest request,
        CancellationToken cancellationToken)
    {
        var model = NormalizeModel(profile.MainlineModel);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(DefaultTimeout);
        var requestToken = timeoutCts.Token;

        try
        {
            return await SendAndParseAsync(profile, model, request, useJsonMode: true, requestToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex) when (!cancellationToken.IsCancellationRequested)
        {
            return new CodingAgentResponse { Error = $"AI 自动编码请求发送失败：{ex.Message}" };
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            return new CodingAgentResponse { Error = $"AI 自动编码请求超过 {DefaultTimeout.TotalMinutes:0} 分钟仍未完成。{ex.Message}" };
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested && ex is UriFormatException or ArgumentException or InvalidOperationException)
        {
            return new CodingAgentResponse { Error = $"AI 自动编码配置无效：{ex.Message}" };
        }
    }

    private async Task<CodingAgentResponse> SendAndParseAsync(
        BackendProfile profile,
        string model,
        CodingAgentRequest request,
        bool useJsonMode,
        CancellationToken requestToken)
    {
        using var message = CreateRequest(profile, model, request, useJsonMode);
        using var response = await _httpClient.SendAsync(message, requestToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync(requestToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            if (useJsonMode && IsJsonModeUnsupported(response, content))
            {
                return await SendAndParseAsync(profile, model, request, useJsonMode: false, requestToken).ConfigureAwait(false);
            }

            return new CodingAgentResponse
            {
                Error = BuildFailureMessage(response, content, model),
                RawJson = content
            };
        }

        if (!TryExtractAssistantText(content, out var assistantText, out var extractError))
        {
            return new CodingAgentResponse
            {
                Error = extractError,
                RawJson = content,
                Message = Snippet(content)
            };
        }

        return ParseAssistantResponse(assistantText!, content, request.MaxFileChanges, request.MaxCommands);
    }

    private static HttpRequestMessage CreateRequest(BackendProfile profile, string model, CodingAgentRequest request, bool useJsonMode)
    {
        var baseUrl = BackendProtocol.NormalizeBaseUrl(profile.BaseUrl, BackendProtocol.ChatCompletionsImageJson).TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException("AI 自动编码后端地址为空，请在设置中配置 Base URL。");
        }

        var uri = new Uri($"{baseUrl}/chat/completions");
        var payload = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["messages"] = new object[]
            {
                new { role = "system", content = BuildSystemPrompt() },
                new { role = "user", content = BuildUserPrompt(request) }
            },
            ["temperature"] = 0.2
        };
        if (useJsonMode)
        {
            payload["response_format"] = new { type = "json_object" };
        }
        var json = JsonSerializer.Serialize(payload, SerializerOptions);
        var message = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        ApplyAuthorization(message, profile.ApiKey);
        return message;
    }

    private static string BuildSystemPrompt()
    {
        return string.Join(Environment.NewLine, new[]
        {
            "你是一个谨慎的 AI 自动编码助手，运行在一个 WPF 桌面应用里。你只能提出计划、文件变更提案和命令建议，不能声称自己已经修改文件或执行命令。",
            "",
            "安全要求：",
            "- 不要要求读取或修改 .env、密钥、token、证书、secrets 文件。",
            "- 不要输出、猜测或要求用户粘贴 API key、password、token。",
            "- 不要提出删除文件、git push、git reset、git clean、安装依赖或下载执行脚本作为自动命令。",
            "- 文件路径必须是相对工作区路径，不能是绝对路径，不能包含 ..。",
            "- 对现有文件的 replace 提案必须带 originalSha256，使用上下文里提供的 sha256。",
            "",
            "会话模式：chat=只回答不改文件；clarify=先澄清需求；cowork=可提出读取/修改/验证方案；code=专注代码编辑；acp=按计划-实现-审查组织步骤。",
            "无论哪种模式，都必须遵守审批流：只能提出文件变更和命令建议，等待用户批准。",
            "",
            "只返回一个 JSON 对象，不要使用 Markdown 代码块。格式：",
            "{",
            "  \"message\": \"给用户看的简短说明\",",
            "  \"plan\": [\"步骤 1\", \"步骤 2\"],",
            "  \"fileChanges\": [",
            "    {",
            "      \"relativePath\": \"相对路径\",",
            "      \"changeType\": \"create 或 replace\",",
            "      \"originalSha256\": \"replace 时必填，create 可为 null\",",
            "      \"summary\": \"这次文件变更的摘要\",",
            "      \"proposedContent\": \"完整文件内容\"",
            "    }",
            "  ],",
            "  \"commands\": [",
            "    {",
            "      \"command\": \"dotnet test\",",
            "      \"workingDirectory\": \".\",",
            "      \"reason\": \"为什么建议运行\"",
            "    }",
            "  ]",
            "}",
            "",
            "如果信息不足，先返回 plan 和 message，不要编造文件内容。"
        });
    }

    private static string BuildUserPrompt(CodingAgentRequest request)
    {
        var builder = new StringBuilder();
        builder.AppendLine("会话模式：");
        builder.AppendLine(string.IsNullOrWhiteSpace(request.SessionMode) ? CodingSessionMode.Cowork : request.SessionMode.Trim());
        builder.AppendLine();
        builder.AppendLine("用户目标：");
        builder.AppendLine(request.Goal.Trim());
        builder.AppendLine();
        builder.AppendLine("工作区文件树（相对路径）：");
        foreach (var item in request.WorkspaceSnapshot.FileTree)
        {
            builder.AppendLine("- " + item);
        }

        if (request.WorkspaceSnapshot.SkippedFiles.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("未提供给模型的文件：");
            foreach (var item in request.WorkspaceSnapshot.SkippedFiles.Take(60))
            {
                builder.AppendLine("- " + item);
            }
        }

        builder.AppendLine();
        builder.AppendLine("已提供的文件内容：");
        foreach (var file in request.WorkspaceSnapshot.Files)
        {
            builder.AppendLine($"--- FILE {file.RelativePath} sha256={file.Sha256} bytes={file.ByteLength} ---");
            builder.AppendLine(file.Content);
            builder.AppendLine($"--- END FILE {file.RelativePath} ---");
        }

        builder.AppendLine();
        builder.AppendLine($"最多返回 {request.MaxFileChanges} 个 fileChanges 和 {request.MaxCommands} 个 commands。");
        return builder.ToString();
    }

    private static bool TryExtractAssistantText(string responseJson, out string? text, out string? error)
    {
        text = null;
        error = null;
        try
        {
            using var document = JsonDocument.Parse(responseJson);
            var root = document.RootElement;
            if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array)
            {
                foreach (var choice in choices.EnumerateArray())
                {
                    if (choice.TryGetProperty("message", out var message)
                        && message.TryGetProperty("content", out var content))
                    {
                        text = ExtractContentText(content);
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            return true;
                        }
                    }
                }
            }

            error = $"AI 自动编码接口未返回文本内容。响应片段：{Snippet(responseJson)}";
            return false;
        }
        catch (JsonException ex)
        {
            error = $"AI 自动编码接口返回非 JSON 响应：{ex.Message}。响应片段：{Snippet(responseJson)}";
            return false;
        }
    }

    private static string? ExtractContentText(JsonElement content)
    {
        if (content.ValueKind == JsonValueKind.String)
        {
            return content.GetString();
        }

        if (content.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var builder = new StringBuilder();
        foreach (var item in content.EnumerateArray())
        {
            if (item.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
            {
                builder.Append(text.GetString());
            }
            else if (item.TryGetProperty("content", out var nested) && nested.ValueKind == JsonValueKind.String)
            {
                builder.Append(nested.GetString());
            }
        }

        return builder.ToString();
    }

    private static CodingAgentResponse ParseAssistantResponse(string assistantText, string rawProviderJson, int maxFileChanges, int maxCommands)
    {
        var json = ExtractJsonObject(assistantText);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new CodingAgentResponse
            {
                Message = assistantText.Trim(),
                RawJson = rawProviderJson,
                Error = "模型未返回可解析的 JSON 对象。"
            };
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            return new CodingAgentResponse
            {
                Message = GetString(root, "message") ?? "",
                Plan = ReadStringArray(root, "plan"),
                FileChanges = ReadFileChanges(root, maxFileChanges),
                Commands = ReadCommands(root, maxCommands),
                RawJson = rawProviderJson
            };
        }
        catch (JsonException ex)
        {
            return new CodingAgentResponse
            {
                Message = assistantText.Trim(),
                RawJson = rawProviderJson,
                Error = $"模型返回的 JSON 无法解析：{ex.Message}"
            };
        }
    }

    private static IReadOnlyList<CodingFileChangeProposalDraft> ReadFileChanges(JsonElement root, int maxFileChanges)
    {
        if (!root.TryGetProperty("fileChanges", out var changes) || changes.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<CodingFileChangeProposalDraft>();
        }

        return changes.EnumerateArray()
            .Select(item => new CodingFileChangeProposalDraft
            {
                RelativePath = GetString(item, "relativePath") ?? "",
                ChangeType = GetString(item, "changeType") ?? CodingFileChangeType.Replace,
                OriginalSha256 = GetString(item, "originalSha256"),
                Summary = GetString(item, "summary") ?? "",
                ProposedContent = GetString(item, "proposedContent") ?? ""
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.RelativePath))
            .Take(Math.Max(0, maxFileChanges))
            .ToList();
    }

    private static IReadOnlyList<CodingCommandProposalDraft> ReadCommands(JsonElement root, int maxCommands)
    {
        if (!root.TryGetProperty("commands", out var commands) || commands.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<CodingCommandProposalDraft>();
        }

        return commands.EnumerateArray()
            .Select(item => new CodingCommandProposalDraft
            {
                Command = GetString(item, "command") ?? "",
                WorkingDirectory = GetString(item, "workingDirectory") ?? ".",
                Reason = GetString(item, "reason") ?? ""
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Command))
            .Take(Math.Max(0, maxCommands))
            .ToList();
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var values) || values.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        return values.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString() ?? "")
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToList();
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static string ExtractJsonObject(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = trimmed.IndexOf('\n');
            var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewline >= 0 && lastFence > firstNewline)
            {
                trimmed = trimmed[(firstNewline + 1)..lastFence].Trim();
            }
        }

        if (TryParseObject(trimmed, out var wholeObject))
        {
            return wholeObject;
        }

        for (var start = 0; start < trimmed.Length; start++)
        {
            if (trimmed[start] != '{')
            {
                continue;
            }

            var depth = 0;
            var inString = false;
            var escaped = false;
            for (var index = start; index < trimmed.Length; index++)
            {
                var current = trimmed[index];
                if (inString)
                {
                    if (escaped)
                    {
                        escaped = false;
                    }
                    else if (current == '\\')
                    {
                        escaped = true;
                    }
                    else if (current == '"')
                    {
                        inString = false;
                    }

                    continue;
                }

                if (current == '"')
                {
                    inString = true;
                }
                else if (current == '{')
                {
                    depth++;
                }
                else if (current == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        var candidate = trimmed[start..(index + 1)];
                        if (TryParseObject(candidate, out var parsed))
                        {
                            return parsed;
                        }

                        break;
                    }
                }
            }
        }

        return "";
    }

    private static bool TryParseObject(string value, out string json)
    {
        json = "";
        try
        {
            using var document = JsonDocument.Parse(value);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            json = value;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsJsonModeUnsupported(HttpResponseMessage response, string content)
    {
        if ((int)response.StatusCode is not (400 or 422))
        {
            return false;
        }

        return content.Contains("response_format", StringComparison.OrdinalIgnoreCase)
               || content.Contains("json_object", StringComparison.OrdinalIgnoreCase)
               || content.Contains("JSON mode", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildFailureMessage(HttpResponseMessage response, string content, string model)
    {
        var providerMessage = ExtractErrorMessage(content);
        var detail = string.IsNullOrWhiteSpace(providerMessage) ? Snippet(content) : providerMessage;
        var suffix = IsModelUnavailableError(detail)
            ? $"当前模型 {model} 在该后端不可用；请在设置里换成后端支持的聊天模型，或使用默认 {DefaultCodingModel}。"
            : "请确认后端支持 /v1/chat/completions 且模型可用。";
        return $"AI 自动编码请求失败：HTTP {(int)response.StatusCode} {response.ReasonPhrase}。{detail}。{suffix}";
    }

    private static string ExtractErrorMessage(string content)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;
            if (root.TryGetProperty("error", out var error))
            {
                if (error.ValueKind == JsonValueKind.Object && error.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
                {
                    return message.GetString() ?? "";
                }

                if (error.ValueKind == JsonValueKind.String)
                {
                    return error.GetString() ?? "";
                }
            }

            if (root.TryGetProperty("message", out var rootMessage) && rootMessage.ValueKind == JsonValueKind.String)
            {
                return rootMessage.GetString() ?? "";
            }
        }
        catch (JsonException)
        {
        }

        return "";
    }

    private static bool IsModelUnavailableError(string error)
    {
        return error.Contains("No available AI provider", StringComparison.OrdinalIgnoreCase)
               || error.Contains("model", StringComparison.OrdinalIgnoreCase)
               && error.Contains("available", StringComparison.OrdinalIgnoreCase);
    }

    private static void ApplyAuthorization(HttpRequestMessage message, string apiKey)
    {
        var trimmed = apiKey.Trim();
        if (trimmed.StartsWith("Authorization:", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed["Authorization:".Length..].Trim();
        }

        if (trimmed.Contains(' '))
        {
            var split = trimmed.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (split.Length == 2)
            {
                message.Headers.Authorization = new AuthenticationHeaderValue(split[0], split[1]);
                return;
            }
        }

        message.Headers.Authorization = trimmed.StartsWith("ak-", StringComparison.OrdinalIgnoreCase)
            ? AuthenticationHeaderValue.Parse(trimmed)
            : new AuthenticationHeaderValue("Bearer", trimmed);
    }

    private static string NormalizeModel(string model)
    {
        var value = model.Trim();
        if (string.IsNullOrWhiteSpace(value)
            || string.Equals(value, "auto", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "gpt-5.5", StringComparison.OrdinalIgnoreCase))
        {
            return DefaultCodingModel;
        }

        return value;
    }

    private static string Snippet(string value)
    {
        var normalized = value.Replace("\r", " ").Replace("\n", " ").Trim();
        return normalized.Length <= 500 ? normalized : normalized[..500] + "...";
    }
}
