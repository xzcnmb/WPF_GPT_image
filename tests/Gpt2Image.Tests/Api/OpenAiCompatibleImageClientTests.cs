using System.IO;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Gpt2Image.Core.Api;
using Gpt2Image.Core.Models;
using Microsoft.Extensions.Logging;

namespace Gpt2Image.Tests.Api;

public sealed class OpenAiCompatibleImageClientTests
{
    [Fact]
    public void Constructor_disables_default_http_client_timeout_for_long_image_generation()
    {
        var httpClient = new HttpClient(new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)));

        _ = new OpenAiCompatibleImageClient(httpClient);

        Assert.Equal(Timeout.InfiniteTimeSpan, httpClient.Timeout);
    }

    [Fact]
    public async Task GenerateAsync_posts_images_generation_request_and_parses_base64_outputs()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"data\":[{\"b64_json\":\"aW1hZ2U=\",\"revised_prompt\":\"clean prompt\"}],\"usage\":{\"total_tokens\":42}}",
                Encoding.UTF8,
                "application/json")
        });
        var client = new OpenAiCompatibleImageClient(new HttpClient(handler));
        var profile = TestProfile();

        var result = await client.GenerateAsync(profile, new ImageGenerationRequest
        {
            Prompt = "draw a workstation",
            Size = "1024x1024",
            Quality = "high",
            OutputFormat = "png",
            Count = 1
        }, CancellationToken.None);

        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("https://example.test/v1/images/generations", handler.Request.RequestUri!.ToString());
        Assert.Equal("Bearer secret", handler.Request.Headers.Authorization?.ToString());
        var body = handler.RequestBody!;
        Assert.Contains("\"model\":\"gpt-image-2\"", body);
        Assert.Contains("\"response_format\":\"b64_json\"", body);
        Assert.Equal("aW1hZ2U=", result.Images.Single().Base64);
        Assert.Equal("clean prompt", result.Images.Single().RevisedPrompt);
        Assert.Equal(42, result.Usage?.TotalTokens);
    }

    [Fact]
    public async Task GenerateAsync_posts_images_generation_url_format_and_downloads_image_for_preview()
    {
        var handler = new SequenceHandler(
            request => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"data\":[{\"url\":\"https://cdn.example.test/generated.png\",\"revised_prompt\":\"clean prompt\"}],\"usage\":{\"total_tokens\":21}}",
                    Encoding.UTF8,
                    "application/json")
            },
            request => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Encoding.ASCII.GetBytes("image-bytes"))
            });
        var client = new OpenAiCompatibleImageClient(new HttpClient(handler));

        var result = await client.GenerateAsync(TestProfile(), new ImageGenerationRequest
        {
            Prompt = "喜羊羊正在跪着跟懒洋洋求婚，在青青草原上",
            Size = "3840x2048",
            ResponseFormat = "url",
            Count = 1
        }, CancellationToken.None);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("https://example.test/v1/images/generations", handler.Requests[0].RequestUri!.ToString());
        Assert.Equal("https://cdn.example.test/generated.png", handler.Requests[1].RequestUri!.ToString());
        var body = handler.RequestBodies[0];
        Assert.Contains("\"model\":\"gpt-image-2\"", body);
        Assert.Contains("\"size\":\"3840x2048\"", body);
        Assert.Contains("\"n\":1", body);
        Assert.Contains("\"response_format\":\"url\"", body);
        Assert.DoesNotContain("\"quality\":\"auto\"", body);
        Assert.DoesNotContain("\"output_format\":\"png\"", body);
        Assert.Equal(Convert.ToBase64String(Encoding.ASCII.GetBytes("image-bytes")), result.Images.Single().Base64);
        Assert.Equal("clean prompt", result.Images.Single().RevisedPrompt);
        Assert.Equal(21, result.Usage?.TotalTokens);
    }

    [Fact]
    public async Task GenerateAsync_sends_raw_ak_authorization_header_for_routin_images_api()
    {
        var handler = new SequenceHandler(
            request => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"data\":[{\"url\":\"https://means.asia/s3/gpt-image/images/20260606/generated.png\",\"revised_prompt\":\"成功提示词\"}],\"successful\":true,\"usage\":{\"total_tokens\":3224}}",
                    Encoding.UTF8,
                    "application/json")
            },
            request => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Encoding.ASCII.GetBytes("routin-image"))
            });
        var client = new OpenAiCompatibleImageClient(new HttpClient(handler));
        var profile = TestProfile(
            baseUrl: "https://api.routin.ai/v1/images/generations",
            apiKey: "ak-test-key",
            imageModel: "gpt-image-2-all");

        var result = await client.GenerateAsync(profile, new ImageGenerationRequest
        {
            Prompt = "喜羊羊正在跪着跟懒洋洋求婚，在青青草原上",
            Size = "1024x1024",
            ResponseFormat = "url",
            Count = 1
        }, CancellationToken.None);

        Assert.Equal("https://api.routin.ai/v1/images/generations", handler.Requests[0].RequestUri!.ToString());
        Assert.Equal("ak-test-key", handler.Requests[0].Headers.Authorization?.ToString());
        Assert.Equal("gpt-image-2-all", GetJsonString(handler.RequestBodies[0], "model"));
        Assert.Equal("1024x1024", GetJsonString(handler.RequestBodies[0], "size"));
        Assert.Equal("url", GetJsonString(handler.RequestBodies[0], "response_format"));
        Assert.Equal(Convert.ToBase64String(Encoding.ASCII.GetBytes("routin-image")), result.Images.Single().Base64);
        Assert.Equal("成功提示词", result.Images.Single().RevisedPrompt);
        Assert.Equal(3224, result.Usage?.TotalTokens);
    }

    [Fact]
    public async Task GenerateAsync_logs_full_request_url_and_json_body()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"data\":[{\"b64_json\":\"aW1hZ2U=\"}]}", Encoding.UTF8, "application/json")
        });
        var logger = new CapturingLogger<OpenAiCompatibleImageClient>();
        var client = new OpenAiCompatibleImageClient(new HttpClient(handler), logger);

        await client.GenerateAsync(TestProfile(), new ImageGenerationRequest
        {
            Prompt = "喜羊羊和红太狼在青青草原上面举行婚礼",
            Size = "3840x2048",
            ResponseFormat = "url",
            Count = 1
        }, CancellationToken.None);

        var message = Assert.Single(logger.Messages, item => item.Contains("发送生图请求", StringComparison.Ordinal));
        Assert.Contains("https://example.test/v1/images/generations", message);
        Assert.Contains("\"model\":\"gpt-image-2\"", message);
        Assert.Contains("\"prompt\":\"喜羊羊和红太狼在青青草原上面举行婚礼\"", message);
        Assert.DoesNotContain("\\u559C", message);
        Assert.Contains("\"size\":\"3840x2048\"", message);
        Assert.Contains("\"response_format\":\"url\"", message);
        Assert.DoesNotContain("Bearer", message);
        Assert.DoesNotContain("secret", message);
    }

    [Fact]
    public async Task GenerateAsync_resolves_relative_image_url_against_profile_base_url()
    {
        var handler = new SequenceHandler(
            request => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"data\":[{\"url\":\"/images/generated.png\"}]}",
                    Encoding.UTF8,
                    "application/json")
            },
            request => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Encoding.ASCII.GetBytes("relative-image"))
            });
        var client = new OpenAiCompatibleImageClient(new HttpClient(handler));

        var result = await client.GenerateAsync(TestProfile(), new ImageGenerationRequest
        {
            Prompt = "draw",
            ResponseFormat = "url",
            Count = 1
        }, CancellationToken.None);

        Assert.Equal("https://example.test/images/generated.png", handler.Requests[1].RequestUri!.ToString());
        Assert.Equal(Convert.ToBase64String(Encoding.ASCII.GetBytes("relative-image")), result.Images.Single().Base64);
    }

    [Fact]
    public async Task GenerateAsync_returns_failed_result_when_url_image_download_fails()
    {
        var handler = new SequenceHandler(
            request => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"data\":[{\"url\":\"https://cdn.example.test/generated.png\"}]}",
                    Encoding.UTF8,
                    "application/json")
            },
            request => new HttpResponseMessage(HttpStatusCode.NotFound));
        var client = new OpenAiCompatibleImageClient(new HttpClient(handler));

        var result = await client.GenerateAsync(TestProfile(), new ImageGenerationRequest
        {
            Prompt = "draw",
            ResponseFormat = "url",
            Count = 1
        }, CancellationToken.None);

        Assert.Equal("failed", result.Status);
        Assert.Contains("图片 URL 下载失败", result.Error);
        Assert.Contains("https://cdn.example.test/generated.png", result.Error);
    }

    [Fact]
    public async Task GenerateAsync_adds_v1_segment_when_base_url_omits_it()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"data\":[{\"b64_json\":\"aW1hZ2U=\"}]}", Encoding.UTF8, "application/json")
        });
        var client = new OpenAiCompatibleImageClient(new HttpClient(handler));
        var profile = TestProfile(baseUrl: "https://example.test");

        await client.GenerateAsync(profile, new ImageGenerationRequest
        {
            Prompt = "draw",
            Count = 1
        }, CancellationToken.None);

        Assert.Equal("https://example.test/v1/images/generations", handler.Request!.RequestUri!.ToString());
    }

    [Fact]
    public async Task GenerateAsync_returns_clear_diagnostic_when_success_response_is_not_json()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html>not json</html>", Encoding.UTF8, "text/html")
        });
        var client = new OpenAiCompatibleImageClient(new HttpClient(handler));

        var result = await client.GenerateAsync(TestProfile(), new ImageGenerationRequest
        {
            Prompt = "draw",
            Count = 1
        }, CancellationToken.None);

        Assert.Equal("failed", result.Status);
        Assert.Contains("非 JSON", result.Error);
        Assert.Contains("Content-Type: text/html", result.Error);
        Assert.Contains("/v1", result.Error);
    }

    [Fact]
    public async Task GenerateAsync_returns_failed_result_when_request_stream_ends_prematurely()
    {
        var client = new OpenAiCompatibleImageClient(new HttpClient(new ThrowingHandler(
            new HttpRequestException("The response ended prematurely."))));

        var result = await client.GenerateAsync(TestProfile(), new ImageGenerationRequest
        {
            Prompt = "draw",
            Count = 1
        }, CancellationToken.None);

        Assert.Equal("failed", result.Status);
        Assert.Contains("请求发送失败", result.Error);
        Assert.Contains("prematurely", result.Error);
    }

    [Fact]
    public async Task GenerateAsync_returns_response_snippet_when_success_json_has_no_images()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"object\":\"response\",\"output\":[{\"type\":\"message\",\"content\":[{\"type\":\"output_text\",\"text\":\"no image account available\"}]}]}",
                Encoding.UTF8,
                "application/json")
        });
        var client = new OpenAiCompatibleImageClient(new HttpClient(handler));

        var result = await client.GenerateAsync(TestProfile(protocol: BackendProtocol.OpenAiResponses), new ImageGenerationRequest
        {
            Prompt = "draw",
            Count = 1
        }, CancellationToken.None);

        Assert.Equal("failed", result.Status);
        Assert.Contains("未返回图片", result.Error);
        Assert.Contains("no image account available", result.Error);
    }

    [Fact]
    public async Task GenerateAsync_posts_responses_protocol_and_parses_image_generation_output()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"object\":\"response\",\"output\":[{\"type\":\"image_generation_call\",\"result\":\"cmVzcG9uc2U=\",\"revised_prompt\":\"response prompt\"}],\"usage\":{\"total_tokens\":12}}",
                Encoding.UTF8,
                "application/json")
        });
        var client = new OpenAiCompatibleImageClient(new HttpClient(handler));
        var profile = TestProfile(protocol: BackendProtocol.OpenAiResponses);

        var result = await client.GenerateAsync(profile, new ImageGenerationRequest
        {
            Prompt = "draw a poster",
            Size = "1024x1024",
            Quality = "high",
            Count = 1
        }, CancellationToken.None);

        Assert.Equal("https://example.test/v1/responses", handler.Request!.RequestUri!.ToString());
        Assert.Contains("\"tools\":[{\"type\":\"image_generation\"", handler.RequestBody!);
        Assert.Equal("cmVzcG9uc2U=", result.Images.Single().Base64);
        Assert.Equal("response prompt", result.Images.Single().RevisedPrompt);
        Assert.Equal(12, result.Usage?.TotalTokens);
    }

    [Fact]
    public async Task GenerateAsync_posts_chat_completions_protocol_and_parses_markdown_data_url()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"choices\":[{\"message\":{\"content\":\"![image_1](data:image/png;base64,Y2hhdA==)\"}}],\"usage\":{\"total_tokens\":7}}",
                Encoding.UTF8,
                "application/json")
        });
        var client = new OpenAiCompatibleImageClient(new HttpClient(handler));
        var profile = TestProfile(protocol: BackendProtocol.ChatCompletionsImageJson);

        var result = await client.GenerateAsync(profile, new ImageGenerationRequest
        {
            Prompt = "draw a poster",
            Count = 1
        }, CancellationToken.None);

        Assert.Equal("https://example.test/v1/chat/completions", handler.Request!.RequestUri!.ToString());
        Assert.Contains("\"messages\":[{\"role\":\"user\",\"content\":\"draw a poster\"}]", handler.RequestBody!);
        Assert.Equal("Y2hhdA==", result.Images.Single().Base64);
        Assert.Equal(7, result.Usage?.TotalTokens);
    }

    [Fact]
    public async Task GenerateAsync_posts_images_edit_multipart_when_input_image_is_present()
    {
        var imagePath = CreateTempImageFile("edit.png", Encoding.ASCII.GetBytes("fake-image"));
        try
        {
            var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"data\":[{\"b64_json\":\"aW1hZ2U=\",\"revised_prompt\":\"edited prompt\"}]}",
                    Encoding.UTF8,
                    "application/json")
            });
            var client = new OpenAiCompatibleImageClient(new HttpClient(handler));

            var result = await client.GenerateAsync(TestProfile(), new ImageGenerationRequest
            {
                Prompt = "edit this image",
                Images = new[] { new ImageInputAsset { FilePath = imagePath, MimeType = "image/png" } },
                Count = 1
            }, CancellationToken.None);

            Assert.Equal("https://example.test/v1/images/edits", handler.Request!.RequestUri!.ToString());
            Assert.Equal("multipart/form-data", handler.Request.Content!.Headers.ContentType!.MediaType);
            Assert.Equal("aW1hZ2U=", result.Images.Single().Base64);
        }
        finally
        {
            File.Delete(imagePath);
        }
    }

    [Fact]
    public async Task GenerateAsync_posts_responses_protocol_with_input_image_data_url()
    {
        var imagePath = CreateTempImageFile("input.png", Encoding.ASCII.GetBytes("fake-image"));
        try
        {
            var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"object\":\"response\",\"output\":[{\"type\":\"image_generation_call\",\"result\":\"cmVzcG9uc2U=\"}]}",
                    Encoding.UTF8,
                    "application/json")
            });
            var client = new OpenAiCompatibleImageClient(new HttpClient(handler));

            await client.GenerateAsync(TestProfile(protocol: BackendProtocol.OpenAiResponses), new ImageGenerationRequest
            {
                Prompt = "use this image",
                Images = new[] { new ImageInputAsset { FilePath = imagePath, MimeType = "image/png" } },
                Count = 1
            }, CancellationToken.None);

            Assert.Contains("\"input_image\"", handler.RequestBody!);
            Assert.Contains("data:image/png;base64,", handler.RequestBody!);
        }
        finally
        {
            File.Delete(imagePath);
        }
    }

    [Fact]
    public async Task GenerateAsync_posts_chat_completions_protocol_with_multimodal_input_image()
    {
        var imagePath = CreateTempImageFile("chat.png", Encoding.ASCII.GetBytes("fake-image"));
        try
        {
            var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"choices\":[{\"message\":{\"content\":\"![image_1](data:image/png;base64,Y2hhdA==)\"}}]}",
                    Encoding.UTF8,
                    "application/json")
            });
            var client = new OpenAiCompatibleImageClient(new HttpClient(handler));

            await client.GenerateAsync(TestProfile(protocol: BackendProtocol.ChatCompletionsImageJson), new ImageGenerationRequest
            {
                Prompt = "describe and edit",
                Images = new[] { new ImageInputAsset { FilePath = imagePath, MimeType = "image/png" } },
                Count = 1
            }, CancellationToken.None);

            Assert.Contains("\"type\":\"image_url\"", handler.RequestBody!);
            Assert.Contains("data:image/png;base64,", handler.RequestBody!);
        }
        finally
        {
            File.Delete(imagePath);
        }
    }

    [Fact]
    public async Task ChatAsync_posts_chat_completions_and_parses_assistant_text()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"choices\":[{\"message\":{\"content\":\"你好，我是助手\"}}],\"usage\":{\"prompt_tokens\":10,\"completion_tokens\":5,\"total_tokens\":15}}",
                Encoding.UTF8,
                "application/json")
        });
        var client = new OpenAiCompatibleImageClient(new HttpClient(handler));

        var result = await client.ChatAsync(TestProfile(), new ChatRequest
        {
            Model = "gpt-4o-mini",
            Messages = new[]
            {
                new ChatMessage { Role = "user", Content = "你好" }
            }
        }, CancellationToken.None);

        Assert.Equal("https://example.test/v1/chat/completions", handler.Request!.RequestUri!.ToString());
        Assert.Contains("\"messages\":[{\"role\":\"user\",\"content\":\"你好\"}]", handler.RequestBody!);
        Assert.Equal("你好，我是助手", result.Content);
        Assert.Equal(10, result.Usage?.InputTokens);
        Assert.Equal(5, result.Usage?.OutputTokens);
        Assert.Equal(15, result.Usage?.TotalTokens);
    }

    [Fact]
    public async Task StreamResponsesAsync_emits_partial_and_final_image_events()
    {
        var stream = string.Join("\n", new[]
        {
            "event: response.image_generation_call.partial_image",
            "data: {\"partial_image_index\":0,\"b64_json\":\"cGFydGlhbA==\"}",
            "",
            "event: response.output_item.done",
            "data: {\"item\":{\"type\":\"image_generation_call\",\"result\":\"ZmluYWw=\"}}",
            "",
            "event: response.completed",
            "data: {\"response\":{\"id\":\"resp_1\"}}",
            "",
            ""
        });
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(stream, Encoding.UTF8, "text/event-stream")
        });
        var client = new OpenAiCompatibleImageClient(new HttpClient(handler));

        var events = new List<ImageStreamEvent>();
        await foreach (var item in client.StreamAgentImagesAsync(TestProfile(), new AgentRunRequest
        {
            Goal = "research and generate",
            MaxRounds = 1,
            UseWebSearch = true
        }, CancellationToken.None))
        {
            events.Add(item);
        }

        Assert.Contains(events, e => e.Kind == ImageStreamEventKind.PartialImage && e.Base64 == "cGFydGlhbA==");
        Assert.Contains(events, e => e.Kind == ImageStreamEventKind.FinalImage && e.Base64 == "ZmluYWw=");
        Assert.Contains(events, e => e.Kind == ImageStreamEventKind.Completed && e.ResponseId == "resp_1");
    }

    private static string CreateTempImageFile(string fileName, byte[] bytes)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}_{fileName}");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static BackendProfile TestProfile(
        string baseUrl = "https://example.test/v1",
        string protocol = BackendProtocol.OpenAiImages,
        string apiKey = "secret",
        string imageModel = "gpt-image-2") => new()
    {
        Id = "p1",
        Name = "Test",
        BaseUrl = baseUrl,
        ApiKey = apiKey,
        Protocol = protocol,
        MainlineModel = "gpt-5.5",
        ImageModel = imageModel,
        Concurrency = 1,
        Priority = 0,
        IsEnabled = true
    };

    private static string? GetJsonString(string json, string propertyName)
    {
        using var document = System.Text.Json.JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty(propertyName, out var value) ? value.GetString() : null;
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;

        public RecordingHandler(HttpResponseMessage response)
        {
            _response = response;
        }

        public HttpRequestMessage? Request { get; private set; }
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return _response;
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        private readonly Exception _exception;

        public ThrowingHandler(Exception exception)
        {
            _exception = exception;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            throw _exception;
        }
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

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = new();

        public IDisposable BeginScope<TState>(TState state) => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }
}
