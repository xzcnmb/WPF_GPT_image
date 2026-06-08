using System.Net;
using System.Text;
using System.Text.Json;
using Gpt2Image.Core.Api;
using Gpt2Image.Core.Models;

namespace Gpt2Image.Tests.Api;

public sealed class RoutinXaiVideoClientTests
{
    [Fact]
    public async Task GenerateAsync_posts_create_request_then_polls_done_result()
    {
        var handler = new SequenceHandler(
            _ => JsonResponse(HttpStatusCode.OK, "{\"request_id\":\"req_123\"}"),
            _ => JsonResponse(HttpStatusCode.OK, "{\"status\":\"done\",\"progress\":100,\"video\":{\"url\":\"https://cdn.example.test/video.mp4\",\"duration\":6.5}}"));
        var client = new RoutinXaiVideoClient(new HttpClient(handler));

        var result = await client.GenerateAsync(TestProfile(), new VideoGenerationRequest
        {
            Prompt = "cinematic city street",
            Model = "grok-imagine-video",
            AspectRatio = "16:9",
            Resolution = "720p",
            Duration = 6
        }, CancellationToken.None);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
        Assert.Equal("https://api.routin.ai/xai/v1/videos/generations", handler.Requests[0].RequestUri!.ToString());
        Assert.Equal("ak-test", handler.Requests[0].Headers.Authorization?.ToString());
        Assert.Equal("grok-imagine-video", GetJsonString(handler.RequestBodies[0], "model"));
        Assert.Equal("cinematic city street", GetJsonString(handler.RequestBodies[0], "prompt"));
        Assert.Equal("16:9", GetJsonString(handler.RequestBodies[0], "aspect_ratio"));
        Assert.Equal("720p", GetJsonString(handler.RequestBodies[0], "resolution"));
        Assert.Equal(6, GetJsonInt(handler.RequestBodies[0], "duration"));
        Assert.Equal(HttpMethod.Get, handler.Requests[1].Method);
        Assert.Equal("https://api.routin.ai/xai/v1/videos/req_123", handler.Requests[1].RequestUri!.ToString());

        Assert.Equal("completed", result.Status);
        Assert.Equal("req_123", result.ProviderRequestId);
        var output = Assert.Single(result.Videos);
        Assert.Equal("https://cdn.example.test/video.mp4", output.Url);
        Assert.Equal("video/mp4", output.MimeType);
        Assert.Equal(6.5, output.DurationSeconds);
    }

    [Fact]
    public async Task GenerateAsync_retries_without_model_when_provider_unavailable()
    {
        var handler = new SequenceHandler(
            _ => JsonResponse(HttpStatusCode.BadGateway, "{\"error\":{\"code\":\"no_available_provider\",\"message\":\"No available xAI video provider\"}}"),
            _ => JsonResponse(HttpStatusCode.OK, "{\"request_id\":\"req_retry\"}"),
            _ => JsonResponse(HttpStatusCode.OK, "{\"status\":\"done\",\"video\":{\"url\":\"https://cdn.example.test/retry.mp4\"}}"));
        var client = new RoutinXaiVideoClient(new HttpClient(handler));

        var result = await client.GenerateAsync(TestProfile(), new VideoGenerationRequest
        {
            Prompt = "a cat running",
            Model = "grok-imagine-video",
            AspectRatio = "16:9",
            Resolution = "720p"
        }, CancellationToken.None);

        Assert.Equal(3, handler.Requests.Count);
        Assert.Contains("\"model\":\"grok-imagine-video\"", handler.RequestBodies[0]);
        Assert.DoesNotContain("\"model\"", handler.RequestBodies[1]);
        Assert.Equal("completed", result.Status);
        Assert.Equal("https://cdn.example.test/retry.mp4", Assert.Single(result.Videos).Url);
    }

    [Fact]
    public async Task GenerateAsync_returns_failed_result_for_string_error_payload()
    {
        var handler = new SequenceHandler(
            _ => JsonResponse(HttpStatusCode.BadRequest, "{\"error\":\"bad api key\"}"));
        var client = new RoutinXaiVideoClient(new HttpClient(handler));

        var result = await client.GenerateAsync(TestProfile(), new VideoGenerationRequest
        {
            Prompt = "test",
            AspectRatio = "16:9",
            Resolution = "720p"
        }, CancellationToken.None);

        Assert.Equal("failed", result.Status);
        Assert.Contains("bad api key", result.Error);
    }

    private static BackendProfile TestProfile() => new()
    {
        Id = "routin-video",
        Name = "Routin",
        BaseUrl = "https://api.routin.ai",
        ApiKey = "ak-test",
        Protocol = BackendProtocol.RoutinXaiVideo,
        VideoModel = "grok-imagine-video",
        Concurrency = 1,
        Priority = 0,
        IsEnabled = true
    };

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json) => new(statusCode)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private static string? GetJsonString(string json, string propertyName)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty(propertyName, out var value) ? value.GetString() : null;
    }

    private static int? GetJsonInt(string json, string propertyName)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty(propertyName, out var value) && value.TryGetInt32(out var number) ? number : null;
    }

    private sealed class SequenceHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses;

        public SequenceHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] responses)
        {
            _responses = new Queue<Func<HttpRequestMessage, HttpResponseMessage>>(responses);
        }

        public List<HttpRequestMessage> Requests { get; } = new();

        public List<string> RequestBodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            RequestBodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken));
            return _responses.Dequeue().Invoke(request);
        }
    }
}
