using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Gpt2Image.Core.Models;
using Microsoft.Extensions.Logging;

namespace Gpt2Image.Core.Api;

public sealed class RoutinXaiVideoClient : IVideoGenerationClient
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan GenerationTimeout = TimeSpan.FromMinutes(15);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<RoutinXaiVideoClient>? _logger;

    public RoutinXaiVideoClient(
        HttpClient httpClient,
        ILogger<RoutinXaiVideoClient>? logger = null)
    {
        _httpClient = httpClient;
        _httpClient.Timeout = Timeout.InfiniteTimeSpan;
        _logger = logger;
    }

    public async Task<VideoGenerationResult> GenerateAsync(
        BackendProfile profile,
        VideoGenerationRequest request,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(GenerationTimeout);
        var token = timeoutCts.Token;

        try
        {
            string? requestId;
            try
            {
                requestId = await CreateVideoAsync(profile, request, token).ConfigureAwait(false);
            }
            catch (HttpRequestException ex) when (IsVideoProviderUnavailable(ex) && !string.IsNullOrWhiteSpace(request.Model))
            {
                _logger?.LogWarning(ex, "Routin 视频模型 {Model} 暂无可用 provider，按文档可选 model 字段重试一次，改为不显式传 model。", request.Model);
                requestId = await CreateVideoAsync(profile, CopyWithoutModel(request), token).ConfigureAwait(false);
            }

            if (string.IsNullOrWhiteSpace(requestId))
            {
                return new VideoGenerationResult
                {
                    Status = "failed",
                    Error = "Routin 视频接口未返回 request_id。"
                };
            }

            _logger?.LogInformation("Routin 视频任务已提交，request_id {RequestId}", requestId);
            return await PollVideoAsync(profile, requestId, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new VideoGenerationResult
            {
                Status = "failed",
                Error = $"等待 Routin 视频生成超过 {GenerationTimeout.TotalMinutes:0} 分钟。"
            };
        }
        catch (HttpRequestException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger?.LogWarning(ex, "Routin 视频请求发送失败");
            return new VideoGenerationResult
            {
                Status = "failed",
                Error = $"Routin 视频请求发送失败：{ex.Message}"
            };
        }
        catch (JsonException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger?.LogWarning(ex, "Routin 视频响应 JSON 解析失败");
            return new VideoGenerationResult
            {
                Status = "failed",
                Error = $"Routin 视频响应 JSON 解析失败：{ex.Message}"
            };
        }
    }

    private async Task<string?> CreateVideoAsync(
        BackendProfile profile,
        VideoGenerationRequest request,
        CancellationToken cancellationToken)
    {
        var endpoint = BuildEndpoint(profile.BaseUrl, "videos/generations");
        using var message = new HttpRequestMessage(HttpMethod.Post, endpoint);
        AddAuthorizationHeader(message, profile.ApiKey);
        message.Content = new StringContent(JsonSerializer.Serialize(BuildCreateBody(request), JsonOptions), Encoding.UTF8, "application/json");

        var requestBody = await message.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        _logger?.LogInformation("发送 Routin 视频生成请求，地址 {Endpoint}，请求体 {RequestBody}", endpoint, requestBody);

        using var response = await _httpClient.SendAsync(message, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"HTTP {(int)response.StatusCode}：{ExtractError(content)}");
        }

        using var document = JsonDocument.Parse(content);
        if (document.RootElement.TryGetProperty("request_id", out var requestId))
        {
            return requestId.GetString();
        }

        if (document.RootElement.TryGetProperty("error", out var error))
        {
            throw new HttpRequestException(ExtractError(error));
        }

        return null;
    }

    private async Task<VideoGenerationResult> PollVideoAsync(
        BackendProfile profile,
        string requestId,
        CancellationToken cancellationToken)
    {
        var endpoint = BuildEndpoint(profile.BaseUrl, $"videos/{Uri.EscapeDataString(requestId)}");
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var message = new HttpRequestMessage(HttpMethod.Get, endpoint);
            AddAuthorizationHeader(message, profile.ApiKey);

            using var response = await _httpClient.SendAsync(message, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
            var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                if (IsTransientPollNotReady(response, content))
                {
                    _logger?.LogInformation("Routin 视频任务 {RequestId} 查询结果暂未就绪，HTTP {StatusCode}，继续轮询。响应：{Content}", requestId, (int)response.StatusCode, content);
                    await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                return new VideoGenerationResult
                {
                    Status = "failed",
                    ProviderRequestId = requestId,
                    Error = $"查询 Routin 视频任务失败：HTTP {(int)response.StatusCode}，{ExtractError(content)}"
                };
            }

            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;
            var status = GetString(root, "status") ?? "pending";
            var progress = GetInt32(root, "progress");
            _logger?.LogInformation("Routin 视频任务 {RequestId} 状态 {Status}，进度 {Progress}", requestId, status, progress);

            if (string.Equals(status, "done", StringComparison.OrdinalIgnoreCase))
            {
                return BuildCompletedResult(root, requestId);
            }

            if (string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, "expired", StringComparison.OrdinalIgnoreCase))
            {
                return new VideoGenerationResult
                {
                    Status = status,
                    ProviderRequestId = requestId,
                    Progress = progress,
                    Error = ExtractError(root)
                };
            }

            await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    private static VideoGenerationResult BuildCompletedResult(
        JsonElement root,
        string requestId)
    {
        if (!root.TryGetProperty("video", out var video) || video.ValueKind != JsonValueKind.Object)
        {
            return new VideoGenerationResult
            {
                Status = "failed",
                ProviderRequestId = requestId,
                Error = "Routin 视频任务完成，但响应中缺少 video 对象。"
            };
        }

        var videoUrl = GetString(video, "url");
        if (string.IsNullOrWhiteSpace(videoUrl))
        {
            return new VideoGenerationResult
            {
                Status = "failed",
                ProviderRequestId = requestId,
                Error = "Routin 视频任务完成，但响应中缺少 video.url。"
            };
        }

        if (!Uri.TryCreate(videoUrl, UriKind.Absolute, out var videoUri)
            || (videoUri.Scheme != Uri.UriSchemeHttp && videoUri.Scheme != Uri.UriSchemeHttps))
        {
            return new VideoGenerationResult
            {
                Status = "failed",
                ProviderRequestId = requestId,
                Error = $"Routin 视频任务完成，但 video.url 不是可播放的 HTTP/HTTPS 地址：{videoUrl}"
            };
        }

        var duration = GetDouble(video, "duration");
        var progress = GetInt32(root, "progress");
        return new VideoGenerationResult
        {
            GenerationId = requestId,
            Status = "completed",
            ProviderRequestId = requestId,
            Progress = progress,
            Videos = new[]
            {
                new GeneratedVideoOutput
                {
                    Index = 0,
                    FilePath = "",
                    Url = videoUrl,
                    MimeType = "video/mp4",
                    DurationSeconds = duration,
                    OutputRole = "final"
                }
            }
        };
    }

    private static bool IsTransientPollNotReady(HttpResponseMessage response, string content)
    {
        return response.StatusCode == System.Net.HttpStatusCode.NotFound
            && (content.Contains("Failed to read static file", StringComparison.OrdinalIgnoreCase)
                || content.Contains("Some requested entity was not found", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(content));
    }

    private static VideoGenerationRequest CopyWithoutModel(VideoGenerationRequest request)
    {
        return new VideoGenerationRequest
        {
            Prompt = request.Prompt,
            Duration = request.Duration,
            AspectRatio = request.AspectRatio,
            Resolution = request.Resolution,
            Image = request.Image,
            ReferenceImages = request.ReferenceImages
        };
    }

    private static bool IsVideoProviderUnavailable(Exception exception)
    {
        return exception.Message.Contains("no_available_provider", StringComparison.OrdinalIgnoreCase)
            || exception.Message.Contains("No available xAI video provider", StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<string, object> BuildCreateBody(VideoGenerationRequest request)
    {
        var body = new Dictionary<string, object>
        {
            ["prompt"] = request.Prompt,
            ["aspect_ratio"] = request.AspectRatio,
            ["resolution"] = request.Resolution
        };

        if (!string.IsNullOrWhiteSpace(request.Model))
        {
            body["model"] = request.Model.Trim();
        }

        if (request.Duration is > 0)
        {
            body["duration"] = request.Duration.Value;
        }

        if (request.Image is not null && !string.IsNullOrWhiteSpace(request.Image.Url))
        {
            body["image"] = new Dictionary<string, string> { ["url"] = request.Image.Url.Trim() };
        }

        var references = request.ReferenceImages
            .Where(item => !string.IsNullOrWhiteSpace(item.Url))
            .Select(item => new Dictionary<string, string> { ["url"] = item.Url.Trim() })
            .ToList();
        if (references.Count > 0)
        {
            body["reference_images"] = references;
        }

        return body;
    }

    private static Uri BuildEndpoint(string baseUrl, string path)
    {
        var normalized = BackendProtocol.NormalizeBaseUrl(baseUrl, BackendProtocol.RoutinXaiVideo).TrimEnd('/');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            normalized = "https://api.routin.ai";
        }

        var prefix = normalized.EndsWith("/xai/v1", StringComparison.OrdinalIgnoreCase)
            ? normalized
            : $"{normalized}/xai/v1";
        return new Uri($"{prefix.TrimEnd('/')}/{path.TrimStart('/')}");
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

        return value.StartsWith("ak-", StringComparison.OrdinalIgnoreCase) || value.Contains(' ', StringComparison.Ordinal)
            ? value
            : $"Bearer {value}";
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static int? GetInt32(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : null;
    }

    private static double? GetDouble(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) ? number : null;
    }

    private static string ExtractError(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return "响应为空";
        }

        try
        {
            using var document = JsonDocument.Parse(content);
            return ExtractError(document.RootElement);
        }
        catch (JsonException)
        {
            return content.Length > 400 ? content[..400] : content;
        }
    }

    private static string ExtractError(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            return element.GetString() ?? "未知错误";
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("error", out var error))
            {
                return ExtractError(error);
            }

            var code = GetString(element, "code");
            var message = GetString(element, "message");
            if (!string.IsNullOrWhiteSpace(code) && !string.IsNullOrWhiteSpace(message))
            {
                return $"{code}: {message}";
            }

            if (!string.IsNullOrWhiteSpace(message))
            {
                return message!;
            }
        }

        return element.GetRawText();
    }
}
