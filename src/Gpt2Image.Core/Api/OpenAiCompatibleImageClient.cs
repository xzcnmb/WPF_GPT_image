using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Gpt2Image.Core.Models;
using Microsoft.Extensions.Logging;

namespace Gpt2Image.Core.Api;

public sealed class OpenAiCompatibleImageClient : IImageGenerationClient
{
    private const string DefaultChatModel = "gpt-4o-mini";
    private readonly HttpClient _httpClient;
    private readonly ILogger<OpenAiCompatibleImageClient>? _logger;
    private static readonly TimeSpan DefaultGenerationTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan DefaultResponseReadTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan DefaultPromptOptimizationTimeout = TimeSpan.FromSeconds(90);
    private static readonly Regex DataImageRegex = new(
        "data:image/[^;]+;base64,(?<base64>[A-Za-z0-9+/=]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex MarkdownImageRegex = new(
        "!\\[[^\\]]*\\]\\((?<url>[^)]+)\\)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public OpenAiCompatibleImageClient(
        HttpClient httpClient,
        ILogger<OpenAiCompatibleImageClient>? logger = null)
    {
        _httpClient = httpClient;
        _httpClient.Timeout = Timeout.InfiniteTimeSpan;
        _logger = logger;
    }

    public async Task<GenerationResult> GenerateAsync(
        BackendProfile profile,
        ImageGenerationRequest request,
        CancellationToken cancellationToken)
    {
        var protocol = BackendProtocol.Normalize(profile.Protocol);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(DefaultGenerationTimeout);
        var requestToken = timeoutCts.Token;
        using var message = BuildGenerateRequest(profile, request, protocol);

        try
        {
            var requestBody = message.Content is null
                ? ""
                : await message.Content.ReadAsStringAsync(requestToken).ConfigureAwait(false);
            _logger?.LogInformation(
                "发送生图请求，协议 {Protocol}，地址 {RequestUri}，模型 {Model}，请求体 {RequestBody}",
                protocol,
                message.RequestUri,
                request.Model ?? profile.ImageModel,
                requestBody);

            var stopwatch = Stopwatch.StartNew();
            using var response = await _httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, requestToken).ConfigureAwait(false);
            var headerElapsed = stopwatch.Elapsed;
            _logger?.LogInformation(
                "生图响应头已返回，协议 {Protocol}，地址 {RequestUri}，HTTP {StatusCode}，等待响应头 {ElapsedSeconds:F1}s，开始读取响应体。",
                protocol,
                message.RequestUri,
                (int)response.StatusCode,
                headerElapsed.TotalSeconds);

            var content = await ReadResponseContentAsync(response, requestToken).ConfigureAwait(false);
            stopwatch.Stop();
            if (!response.IsSuccessStatusCode)
            {
                var error = BuildFailureMessage(response, content);
                _logger?.LogWarning("生图请求失败，协议 {Protocol}，HTTP {StatusCode}，错误 {Error}", protocol, (int)response.StatusCode, error);
                return new GenerationResult
                {
                    Status = "failed",
                    Error = error
                };
            }

            if (!TryParseJson(content, out var document))
            {
                var error = BuildNonJsonDiagnostic(response, content);
                _logger?.LogWarning("生图接口返回非 JSON 响应，协议 {Protocol}，地址 {RequestUri}，响应片段 {Snippet}", protocol, message.RequestUri, Snippet(content));
                return new GenerationResult
                {
                    Status = "failed",
                    Error = error
                };
            }

            using var json = document!;
            var root = json.RootElement;
            var outputs = ParseOutputs(root, protocol);
            _logger?.LogInformation(
                "Image API returned, protocol {Protocol}, address {RequestUri}, HTTP {StatusCode}, elapsed {ElapsedSeconds:F1}s, outputs {ImageCount}, urls {UrlCount}, base64 {Base64Count}",
                protocol,
                message.RequestUri,
                (int)response.StatusCode,
                stopwatch.Elapsed.TotalSeconds,
                outputs.Count,
                outputs.Count(output => !string.IsNullOrWhiteSpace(output.Url)),
                outputs.Count(output => !string.IsNullOrWhiteSpace(output.Base64)));

            outputs = ResolveDataUrlOutputs(outputs);
            if (outputs.Count == 0)
            {
                var error = BuildNoImageDiagnostic(protocol, content);
                _logger?.LogWarning("生图接口返回成功但未找到图片，协议 {Protocol}，地址 {RequestUri}，响应片段 {Snippet}", protocol, message.RequestUri, Snippet(content));
                return new GenerationResult
                {
                    Status = "failed",
                    Error = error
                };
            }

            return new GenerationResult
            {
                Images = outputs,
                RevisedPrompt = outputs.FirstOrDefault()?.RevisedPrompt,
                Usage = ParseUsage(root)
            };
        }
        catch (HttpRequestException ex) when (!cancellationToken.IsCancellationRequested)
        {
            var error = BuildTransportFailureMessage(message.RequestUri, ex);
            _logger?.LogWarning(ex, "生图请求发送失败，协议 {Protocol}，地址 {RequestUri}", protocol, message.RequestUri);
            return new GenerationResult
            {
                Status = "failed",
                Error = error
            };
        }
        catch (TimeoutException ex) when (!cancellationToken.IsCancellationRequested)
        {
            var error = $"{ex.Message} 地址：{message.RequestUri}";
            _logger?.LogWarning(ex, "生图响应体读取超时，协议 {Protocol}，地址 {RequestUri}", protocol, message.RequestUri);
            return new GenerationResult
            {
                Status = "failed",
                Error = error
            };
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            var error = $"等待生图响应头超过 {DefaultGenerationTimeout.TotalMinutes:0} 分钟，可能是后端生成较慢或连接长时间无响应，地址：{message.RequestUri}。{ex.Message}";
            _logger?.LogWarning(ex, "生图请求超时，协议 {Protocol}，地址 {RequestUri}", protocol, message.RequestUri);
            return new GenerationResult
            {
                Status = "failed",
                Error = error
            };
        }
    }

    private async Task<string> ReadResponseContentAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(DefaultResponseReadTimeout);
        var readToken = timeoutCts.Token;

        try
        {
            var stopwatch = Stopwatch.StartNew();
            var content = await response.Content.ReadAsStringAsync(readToken).ConfigureAwait(false);
            stopwatch.Stop();
            _logger?.LogInformation(
                "生图响应体读取完成，HTTP {StatusCode}，耗时 {ElapsedSeconds:F1}s，字符数 {CharLength}。",
                (int)response.StatusCode,
                stopwatch.Elapsed.TotalSeconds,
                content.Length);
            return content;
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"读取生图响应体超过 {DefaultResponseReadTimeout.TotalMinutes:0} 分钟，可能是 b64_json 响应体过大或网络读取过慢。", ex);
        }
    }

    public async Task<PromptOptimizationResult> OptimizePromptAsync(
        BackendProfile profile,
        string prompt,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return new PromptOptimizationResult { Error = "提示词为空" };
        }

        var model = NormalizeMainlineModel(profile.MainlineModel);
        return await OptimizePromptWithModelAsync(profile, prompt, model, allowAutoRetry: true, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<PromptOptimizationResult> OptimizePromptWithModelAsync(
        BackendProfile profile,
        string prompt,
        string model,
        bool allowAutoRetry,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(DefaultPromptOptimizationTimeout);
        var requestToken = timeoutCts.Token;
        using var message = BuildPromptOptimizationRequest(profile, prompt, model);

        try
        {
            var requestBody = message.Content is null
                ? ""
                : await message.Content.ReadAsStringAsync(requestToken).ConfigureAwait(false);
            _logger?.LogInformation(
                "发送提示词优化请求，地址 {RequestUri}，模型 {Model}，请求体 {RequestBody}",
                message.RequestUri,
                model,
                requestBody);

            var stopwatch = Stopwatch.StartNew();
            using var response = await _httpClient.SendAsync(message, requestToken).ConfigureAwait(false);
            var content = await response.Content.ReadAsStringAsync(requestToken).ConfigureAwait(false);
            stopwatch.Stop();

            if (!response.IsSuccessStatusCode)
            {
                var error = BuildFailureMessage(response, content);
                if (allowAutoRetry && !string.Equals(model, DefaultChatModel, StringComparison.OrdinalIgnoreCase) && IsModelUnavailableError(error))
                {
                    _logger?.LogWarning("提示词优化模型 {Model} 不可用，改用 {FallbackModel} 重试。错误 {Error}", model, DefaultChatModel, error);
                    return await OptimizePromptWithModelAsync(profile, prompt, DefaultChatModel, allowAutoRetry: false, cancellationToken)
                        .ConfigureAwait(false);
                }

                _logger?.LogWarning("提示词优化请求失败，HTTP {StatusCode}，错误 {Error}", (int)response.StatusCode, error);
                return new PromptOptimizationResult { Error = error };
            }

            if (!TryParseJson(content, out var document))
            {
                var error = BuildPromptNonJsonDiagnostic(response, content);
                _logger?.LogWarning("提示词优化接口返回非 JSON 响应，地址 {RequestUri}，响应片段 {Snippet}", message.RequestUri, Snippet(content));
                return new PromptOptimizationResult { Error = error };
            }

            using var json = document!;
            var optimizedPrompt = ExtractOptimizedPrompt(json.RootElement);
            if (string.IsNullOrWhiteSpace(optimizedPrompt))
            {
                var error = $"提示词优化接口未返回文本内容。响应片段：{Snippet(content)}";
                _logger?.LogWarning("提示词优化接口未返回文本，地址 {RequestUri}，响应片段 {Snippet}", message.RequestUri, Snippet(content));
                return new PromptOptimizationResult { Error = error };
            }

            _logger?.LogInformation(
                "提示词优化完成，地址 {RequestUri}，耗时 {ElapsedSeconds:F1}s",
                message.RequestUri,
                stopwatch.Elapsed.TotalSeconds);

            return new PromptOptimizationResult { OptimizedPrompt = optimizedPrompt };
        }
        catch (HttpRequestException ex) when (!cancellationToken.IsCancellationRequested)
        {
            var error = BuildTransportFailureMessage(message.RequestUri, ex);
            _logger?.LogWarning(ex, "提示词优化请求发送失败，地址 {RequestUri}", message.RequestUri);
            return new PromptOptimizationResult { Error = error };
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            var error = $"提示词优化超过 {DefaultPromptOptimizationTimeout.TotalSeconds:0} 秒仍未完成，地址：{message.RequestUri}。{ex.Message}";
            _logger?.LogWarning(ex, "提示词优化请求超时，地址 {RequestUri}", message.RequestUri);
            return new PromptOptimizationResult { Error = error };
        }
    }

    public async IAsyncEnumerable<ImageStreamEvent> StreamAgentImagesAsync(
        BackendProfile profile,
        AgentRunRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(
            HttpMethod.Post,
            BuildEndpoint(profile.BaseUrl, "responses", BackendProtocol.OpenAiResponses));
        AddAuthorizationHeader(message, profile.ApiKey);
        var tools = request.UseWebSearch
            ? new object[] { new { type = "web_search" }, new { type = "image_generation", model = request.ImageModel ?? profile.ImageModel, partial_images = 2 } }
            : new object[] { new { type = "image_generation", model = request.ImageModel ?? profile.ImageModel, partial_images = 2 } };
        var body = new
        {
            model = request.MainlineModel ?? profile.MainlineModel,
            input = request.Goal,
            tools,
            tool_choice = "auto",
            stream = true,
            store = false
        };
        message.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
        var requestBody = await message.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        _logger?.LogInformation(
            "发送 Agent 流式生图请求，协议 {Protocol}，地址 {RequestUri}，模型 {Model}，请求体 {RequestBody}",
            BackendProtocol.OpenAiResponses,
            message.RequestUri,
            request.MainlineModel ?? profile.MainlineModel,
            requestBody);

        using var response = await _httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            yield return new ImageStreamEvent { Kind = ImageStreamEventKind.Error, Error = ExtractError(error) ?? $"HTTP {(int)response.StatusCode}" };
            yield break;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream);
        string? eventName = null;
        var dataLines = new List<string>();

        while (!reader.EndOfStream)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync().ConfigureAwait(false);
            if (line is null)
            {
                break;
            }

            if (line.Length == 0)
            {
                foreach (var parsed in ParseSseBlock(eventName, string.Join('\n', dataLines)))
                {
                    yield return parsed;
                }
                eventName = null;
                dataLines.Clear();
                continue;
            }

            if (line.StartsWith("event:", StringComparison.OrdinalIgnoreCase))
            {
                eventName = line["event:".Length..].Trim();
            }
            else if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                dataLines.Add(line["data:".Length..].TrimStart());
            }
        }

        foreach (var parsed in ParseSseBlock(eventName, string.Join('\n', dataLines)))
        {
            yield return parsed;
        }
    }

    private static HttpRequestMessage BuildPromptOptimizationRequest(
        BackendProfile profile,
        string prompt,
        string model)
    {
        const string developerPrompt =
            "你是一名专业的商业影像提示词优化师，擅长把普通描述改写成可直接用于图片生成模型的高质量提示词。"
            + "保留用户原始意图、主体、数量、动作、关系、场景和任何文字要求，不要添加会改变事实的内容。"
            + "补充镜头语言、构图、光线、材质、色彩、风格、画面细节和必要的负面约束。"
            + "如果原始提示词是中文，用中文输出；如果是其他语言，保持同语种。"
            + "只输出优化后的单段提示词，不要解释、标题、编号或 Markdown。";

        var body = new
        {
            model,
            messages = new[]
            {
                new { role = "developer", content = developerPrompt },
                new { role = "user", content = $"原始提示词：\n{prompt.Trim()}" }
            },
            temperature = 0.4,
            max_tokens = 900
        };
        return CreateJsonRequest(profile, "chat/completions", BackendProtocol.ChatCompletionsImageJson, body);
    }

    private static HttpRequestMessage BuildGenerateRequest(
        BackendProfile profile,
        ImageGenerationRequest request,
        string protocol)
    {
        return protocol switch
        {
            BackendProtocol.OpenAiResponses => CreateJsonRequest(profile, "responses", protocol, new
            {
                model = request.Model ?? profile.ImageModel,
                input = new[]
                {
                    new
                    {
                        role = "user",
                        content = new[]
                        {
                            new { type = "input_text", text = request.Prompt }
                        }
                    }
                },
                tools = new[]
                {
                    new
                    {
                        type = "image_generation",
                        size = NullIfAuto(request.Size),
                        quality = NullIfAuto(request.Quality),
                        output_format = request.OutputFormat,
                        background = NullIfAuto(request.Background)
                    }
                },
                tool_choice = "auto",
                stream = false,
                store = false,
                n = Math.Max(1, request.Count)
            }),
            BackendProtocol.ChatCompletionsImageJson => CreateJsonRequest(profile, "chat/completions", protocol, new
            {
                model = request.Model ?? profile.ImageModel,
                messages = new[]
                {
                    new { role = "user", content = request.Prompt }
                },
                n = Math.Max(1, request.Count),
                modalities = new[] { "image", "text" }
            }),
            _ => CreateJsonRequest(profile, "images/generations", protocol, BuildImagesGenerationBody(profile, request))
        };
    }

    private static Dictionary<string, object> BuildImagesGenerationBody(
        BackendProfile profile,
        ImageGenerationRequest request)
    {
        var responseFormat = NormalizeResponseFormat(request.ResponseFormat);
        var body = new Dictionary<string, object>
        {
            ["model"] = request.Model ?? profile.ImageModel,
            ["prompt"] = request.Prompt,
            ["n"] = Math.Max(1, request.Count),
            ["response_format"] = responseFormat
        };

        AddIfNotAuto(body, "size", request.Size);
        AddIfNotAuto(body, "quality", request.Quality);
        AddIfNotAuto(body, "background", request.Background);

        if (!string.Equals(responseFormat, "url", StringComparison.OrdinalIgnoreCase))
        {
            AddIfNotAuto(body, "output_format", request.OutputFormat);
            if (request.OutputCompression is not null)
            {
                body["output_compression"] = request.OutputCompression.Value;
            }
        }

        return body;
    }

    private static void AddIfNotAuto(IDictionary<string, object> body, string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || string.Equals(value, "auto", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        body[name] = value;
    }

    private static string NormalizeResponseFormat(string? value)
    {
        return string.Equals(value, "url", StringComparison.OrdinalIgnoreCase) ? "url" : "b64_json";
    }

    private static HttpRequestMessage CreateJsonRequest(
        BackendProfile profile,
        string path,
        string protocol,
        object body)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, BuildEndpoint(profile.BaseUrl, path, protocol));
        AddAuthorizationHeader(message, profile.ApiKey);
        message.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
        return message;
    }

    private static void AddAuthorizationHeader(HttpRequestMessage message, string apiKey)
    {
        var value = BuildAuthorizationHeader(apiKey);
        if (!string.IsNullOrWhiteSpace(value))
        {
            message.Headers.TryAddWithoutValidation("Authorization", value);
        }
    }

    private static string BuildAuthorizationHeader(string apiKey)
    {
        var value = (apiKey ?? string.Empty).Trim();
        const string headerPrefix = "Authorization:";
        if (value.StartsWith(headerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            value = value[headerPrefix.Length..].Trim();
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        if (value.StartsWith("ak-", StringComparison.OrdinalIgnoreCase) || value.Contains(' ', StringComparison.Ordinal))
        {
            return value;
        }

        return $"Bearer {value}";
    }

    private static string? NullIfAuto(string? value)
    {
        return string.Equals(value, "auto", StringComparison.OrdinalIgnoreCase) ? null : value;
    }

    private static List<GeneratedImageOutput> ResolveDataUrlOutputs(IReadOnlyList<GeneratedImageOutput> outputs)
    {
        var resolved = new List<GeneratedImageOutput>(outputs.Count);
        foreach (var output in outputs)
        {
            if (!string.IsNullOrWhiteSpace(output.Base64) || string.IsNullOrWhiteSpace(output.Url))
            {
                resolved.Add(output);
                continue;
            }

            var base64 = ExtractDataUrlBase64(output.Url!);
            resolved.Add(new GeneratedImageOutput
            {
                Base64 = base64,
                Url = base64 is null ? output.Url : null,
                Index = output.Index,
                Size = output.Size,
                RevisedPrompt = output.RevisedPrompt,
                OutputRole = output.OutputRole
            });
        }

        return resolved;
    }

    private static List<GeneratedImageOutput> ParseOutputs(JsonElement root, string protocol)
    {
        var outputs = new List<GeneratedImageOutput>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        if (protocol == BackendProtocol.OpenAiImages && root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            AddImagesFromElement(data, outputs, seen);
        }

        if (outputs.Count == 0)
        {
            AddImagesFromElement(root, outputs, seen);
        }

        return outputs;
    }

    private static void AddImagesFromElement(
        JsonElement element,
        List<GeneratedImageOutput> outputs,
        HashSet<string> seen)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                TryAddObjectImage(element, outputs, seen);
                foreach (var property in element.EnumerateObject())
                {
                    AddImagesFromElement(property.Value, outputs, seen);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    AddImagesFromElement(item, outputs, seen);
                }
                break;
            case JsonValueKind.String:
                AddImagesFromText(element.GetString(), outputs, seen);
                break;
        }
    }

    private static void TryAddObjectImage(
        JsonElement element,
        List<GeneratedImageOutput> outputs,
        HashSet<string> seen)
    {
        var base64 = GetString(element, "b64_json")
                     ?? GetString(element, "result")
                     ?? GetString(element, "image_base64")
                     ?? GetString(element, "base64");
        var url = GetImageUrl(element);
        var revisedPrompt = GetString(element, "revised_prompt");

        if (!string.IsNullOrWhiteSpace(base64))
        {
            AddOutput(base64, url, revisedPrompt, outputs, seen);
        }
        else if (!string.IsNullOrWhiteSpace(url))
        {
            AddOutput(null, url, revisedPrompt, outputs, seen);
        }
    }

    private static void AddImagesFromText(
        string? text,
        List<GeneratedImageOutput> outputs,
        HashSet<string> seen)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        foreach (Match match in DataImageRegex.Matches(text))
        {
            AddOutput(match.Groups["base64"].Value, null, null, outputs, seen);
        }

        foreach (Match match in MarkdownImageRegex.Matches(text))
        {
            var url = match.Groups["url"].Value.Trim();
            var base64 = ExtractDataUrlBase64(url);
            AddOutput(base64, base64 is null ? url : null, null, outputs, seen);
        }

        var trimmed = text.Trim();
        if ((trimmed.StartsWith("{", StringComparison.Ordinal) || trimmed.StartsWith("[", StringComparison.Ordinal))
            && TryParseJson(trimmed, out var nested))
        {
            using var nestedJson = nested!;
            AddImagesFromElement(nestedJson.RootElement, outputs, seen);
        }
    }

    private static void AddOutput(
        string? base64,
        string? url,
        string? revisedPrompt,
        List<GeneratedImageOutput> outputs,
        HashSet<string> seen)
    {
        base64 = NormalizeBase64(base64);
        var key = !string.IsNullOrWhiteSpace(base64) ? $"b64:{base64}" : $"url:{url}";
        if (string.IsNullOrWhiteSpace(base64) && string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        if (!seen.Add(key))
        {
            return;
        }

        outputs.Add(new GeneratedImageOutput
        {
            Base64 = base64,
            Url = url,
            RevisedPrompt = revisedPrompt,
            Index = outputs.Count
        });
    }

    private static string? GetImageUrl(JsonElement element)
    {
        var direct = GetString(element, "url") ?? GetString(element, "image_url");
        if (!string.IsNullOrWhiteSpace(direct))
        {
            return direct;
        }

        if (element.TryGetProperty("image_url", out var imageUrl)
            && imageUrl.ValueKind == JsonValueKind.Object)
        {
            return GetString(imageUrl, "url");
        }

        return null;
    }

    private static string? NormalizeBase64(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return ExtractDataUrlBase64(value) ?? value.Trim();
    }

    private static string? ExtractDataUrlBase64(string value)
    {
        var match = DataImageRegex.Match(value);
        return match.Success ? match.Groups["base64"].Value : null;
    }

    private static IEnumerable<ImageStreamEvent> ParseSseBlock(string? eventName, string data)
    {
        if (string.IsNullOrWhiteSpace(data) || data == "[DONE]")
        {
            yield break;
        }

        using var document = JsonDocument.Parse(data);
        var root = document.RootElement;

        if (eventName?.Contains("partial_image", StringComparison.OrdinalIgnoreCase) == true)
        {
            yield return new ImageStreamEvent
            {
                Kind = ImageStreamEventKind.PartialImage,
                Base64 = GetString(root, "b64_json") ?? GetString(root, "image_base64"),
                PartialImageIndex = GetInt(root, "partial_image_index")
            };
            yield break;
        }

        if (eventName?.Contains("completed", StringComparison.OrdinalIgnoreCase) == true)
        {
            var response = root.TryGetProperty("response", out var responseElement) ? responseElement : root;
            yield return new ImageStreamEvent
            {
                Kind = ImageStreamEventKind.Completed,
                ResponseId = GetString(response, "id")
            };
            yield break;
        }

        if (TryExtractFinalImage(root, out var imageBase64))
        {
            yield return new ImageStreamEvent
            {
                Kind = ImageStreamEventKind.FinalImage,
                Base64 = imageBase64
            };
        }
    }

    private static bool TryExtractFinalImage(JsonElement root, out string? imageBase64)
    {
        imageBase64 = null;
        if (root.TryGetProperty("item", out var item))
        {
            imageBase64 = GetString(item, "result") ?? GetString(item, "b64_json");
        }
        imageBase64 ??= GetString(root, "result") ?? GetString(root, "b64_json");
        return !string.IsNullOrWhiteSpace(imageBase64);
    }

    private static Uri BuildEndpoint(string baseUrl, string path, string protocol)
    {
        var normalizedBaseUrl = BackendProtocol.NormalizeBaseUrl(baseUrl, protocol).TrimEnd('/') + "/";
        return new Uri(new Uri(normalizedBaseUrl), path.TrimStart('/'));
    }

    private static string BuildFailureMessage(HttpResponseMessage response, string content)
    {
        var extracted = ExtractError(content);
        var baseMessage = string.IsNullOrWhiteSpace(extracted)
            ? $"HTTP {(int)response.StatusCode}"
            : $"HTTP {(int)response.StatusCode}: {extracted}";
        return $"{baseMessage}。如果你填写的是接口根域名，请在设置中确认 Base URL 使用 /v1 兼容入口。";
    }

    private static string BuildNonJsonDiagnostic(HttpResponseMessage response, string content)
    {
        var contentType = response.Content.Headers.ContentType?.ToString();
        return $"后端返回非 JSON 响应，无法解析图片结果。HTTP {(int)response.StatusCode}，Content-Type: {contentType ?? "未知"}。响应片段：{Snippet(content)}。如果你填写的是接口根域名，请确认 Base URL 使用 /v1 兼容入口，或切换到该服务实际支持的接口协议。";
    }

    private static string BuildPromptNonJsonDiagnostic(HttpResponseMessage response, string content)
    {
        var contentType = response.Content.Headers.ContentType?.ToString();
        return $"后端返回非 JSON 响应，无法解析提示词优化结果。HTTP {(int)response.StatusCode}，Content-Type: {contentType ?? "未知"}。响应片段：{Snippet(content)}。请确认主模型支持 /v1/chat/completions。";
    }

    private static string BuildTransportFailureMessage(Uri? requestUri, Exception exception)
    {
        return $"请求发送失败：{exception.Message}。地址：{requestUri}。这通常表示后端网关提前关闭连接、上游不可用，或该协议路径不被当前服务稳定支持。可以换一个接口协议或更换可用模型后重试。";
    }

    private static string BuildNoImageDiagnostic(string protocol, string content)
    {
        return $"API 返回成功，但未返回图片字段。协议：{BackendProtocol.DisplayName(protocol)}。响应片段：{Snippet(content)}。这通常表示模型不支持生图、账号/渠道没有图片额度，或这个接口把图片放在尚未支持的非标准字段里。";
    }

    private static bool TryParseJson(string content, out JsonDocument? document)
    {
        document = null;
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        try
        {
            document = JsonDocument.Parse(content);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string Snippet(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return "<空响应>";
        }

        var normalized = content.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal).Trim();
        return normalized.Length <= 500 ? normalized : normalized[..500] + "...";
    }

    private static string RedactUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
        }

        return Uri.TryCreate(url, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Query)
            ? uri.GetLeftPart(UriPartial.Path) + "?..."
            : url;
    }

    private static string? ExtractError(string content)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;
            if (root.TryGetProperty("error", out var error))
            {
                return error.ValueKind == JsonValueKind.String ? error.GetString() : GetString(error, "message");
            }
        }
        catch (JsonException)
        {
            return string.IsNullOrWhiteSpace(content) ? null : Snippet(content);
        }

        return null;
    }

    private static bool IsModelUnavailableError(string error)
    {
        return error.Contains("No available AI provider", StringComparison.OrdinalIgnoreCase)
               || error.Contains("model", StringComparison.OrdinalIgnoreCase)
               && error.Contains("not", StringComparison.OrdinalIgnoreCase)
               && error.Contains("available", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeMainlineModel(string? model)
    {
        var value = model?.Trim();
        if (string.IsNullOrWhiteSpace(value)
            || string.Equals(value, "auto", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "gpt-5.5", StringComparison.OrdinalIgnoreCase))
        {
            return DefaultChatModel;
        }

        return value;
    }

    private static string? ExtractOptimizedPrompt(JsonElement root)
    {
        if (root.TryGetProperty("choices", out var choices)
            && choices.ValueKind == JsonValueKind.Array)
        {
            foreach (var choice in choices.EnumerateArray())
            {
                if (choice.TryGetProperty("message", out var message))
                {
                    var content = ExtractTextContent(message, "content");
                    if (!string.IsNullOrWhiteSpace(content))
                    {
                        return CleanPromptText(content);
                    }
                }

                var text = GetString(choice, "text");
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return CleanPromptText(text);
                }
            }
        }

        return CleanPromptText(GetString(root, "output_text") ?? GetString(root, "content"));
    }

    private static string? ExtractTextContent(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var content))
        {
            return null;
        }

        if (content.ValueKind == JsonValueKind.String)
        {
            return content.GetString();
        }

        if (content.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var parts = new List<string>();
        foreach (var part in content.EnumerateArray())
        {
            var text = GetString(part, "text");
            if (!string.IsNullOrWhiteSpace(text))
            {
                parts.Add(text);
            }
        }

        return parts.Count == 0 ? null : string.Join("\n", parts);
    }

    private static string? CleanPromptText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = value.Trim();
        text = Regex.Replace(text, "^```[a-zA-Z0-9_-]*\\s*", "", RegexOptions.Singleline).Trim();
        text = Regex.Replace(text, "\\s*```$", "", RegexOptions.Singleline).Trim();

        foreach (var prefix in new[] { "优化后的提示词：", "优化后提示词：", "提示词：", "Prompt:" })
        {
            if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return text[prefix.Length..].Trim();
            }
        }

        return text;
    }

    private static TokenUsage? ParseUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var usage))
        {
            return null;
        }

        return new TokenUsage
        {
            InputTokens = GetInt(usage, "input_tokens"),
            OutputTokens = GetInt(usage, "output_tokens"),
            TotalTokens = GetInt(usage, "total_tokens")
        };
    }

    private static string? GetString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int? GetInt(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : null;
}
