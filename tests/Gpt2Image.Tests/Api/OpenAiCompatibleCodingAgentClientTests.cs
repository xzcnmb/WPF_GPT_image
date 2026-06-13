using System.Net;
using System.Text;
using System.Text.Json;
using Gpt2Image.Core.Api;
using Gpt2Image.Core.Models;

namespace Gpt2Image.Tests.Api;

public sealed class OpenAiCompatibleCodingAgentClientTests
{
    [Fact]
    public async Task GenerateProposalAsync_normalizes_legacy_gpt55_model_and_requests_json_object()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"choices\":[{\"message\":{\"content\":\"{\\\"message\\\":\\\"ok\\\",\\\"plan\\\":[\\\"inspect\\\"],\\\"fileChanges\\\":[],\\\"commands\\\":[]}\"}}]}",
                Encoding.UTF8,
                "application/json")
        });
        var client = new OpenAiCompatibleCodingAgentClient(new HttpClient(handler));

        var result = await client.GenerateProposalAsync(TestProfile(mainlineModel: "gpt-5.5"), TestRequest(), CancellationToken.None);

        Assert.Null(result.Error);
        Assert.Equal("https://example.test/v1/chat/completions", handler.Request!.RequestUri!.ToString());
        using var body = JsonDocument.Parse(handler.RequestBody!);
        Assert.Equal("gpt-4o-mini", body.RootElement.GetProperty("model").GetString());
        Assert.Equal("json_object", body.RootElement.GetProperty("response_format").GetProperty("type").GetString());
        Assert.Equal("inspect", Assert.Single(result.Plan));
    }

    [Fact]
    public async Task GenerateProposalAsync_retries_without_json_mode_when_backend_rejects_response_format()
    {
        var handler = new SequenceHandler(
            new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("{\"error\":{\"message\":\"unsupported response_format json_object\"}}", Encoding.UTF8, "application/json")
            },
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"choices\":[{\"message\":{\"content\":\"{\\\"message\\\":\\\"retry ok\\\",\\\"plan\\\":[],\\\"fileChanges\\\":[],\\\"commands\\\":[]}\"}}]}",
                    Encoding.UTF8,
                    "application/json")
            });
        var client = new OpenAiCompatibleCodingAgentClient(new HttpClient(handler));

        var result = await client.GenerateProposalAsync(TestProfile(), TestRequest(), CancellationToken.None);

        Assert.Null(result.Error);
        Assert.Equal("retry ok", result.Message);
        Assert.Equal(2, handler.RequestBodies.Count);
        Assert.Contains("response_format", handler.RequestBodies[0]);
        Assert.DoesNotContain("response_format", handler.RequestBodies[1]);
    }

    [Fact]
    public async Task GenerateProposalAsync_extracts_provider_error_message_for_unavailable_model()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent(
                "{\"error\":{\"message\":\"No available AI provider for model 'bad-model' across all groups\",\"code\":\"no_available_provider\"}}",
                Encoding.UTF8,
                "application/json")
        });
        var client = new OpenAiCompatibleCodingAgentClient(new HttpClient(handler));

        var result = await client.GenerateProposalAsync(TestProfile(mainlineModel: "bad-model"), TestRequest(), CancellationToken.None);

        Assert.Contains("No available AI provider", result.Error);
        Assert.Contains("bad-model", result.Error);
        Assert.Contains("模型", result.Error);
    }

    [Fact]
    public async Task GenerateProposalAsync_parses_fenced_json_with_braces_inside_file_content()
    {
        var assistant = "```json\n{\"message\":\"ok\",\"plan\":[],\"fileChanges\":[{\"relativePath\":\"Program.cs\",\"changeType\":\"replace\",\"originalSha256\":\"abc\",\"summary\":\"update\",\"proposedContent\":\"public class A { public string Text => \\\"{not root}\\\"; }\"}],\"commands\":[]}\n```";
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                choices = new[] { new { message = new { content = assistant } } }
            }), Encoding.UTF8, "application/json")
        });
        var client = new OpenAiCompatibleCodingAgentClient(new HttpClient(handler));

        var result = await client.GenerateProposalAsync(TestProfile(), TestRequest(), CancellationToken.None);

        Assert.Null(result.Error);
        var change = Assert.Single(result.FileChanges);
        Assert.Contains("{not root}", change.ProposedContent);
    }

    private static BackendProfile TestProfile(string mainlineModel = "gpt-4o-mini") => new()
    {
        Id = "coding-default",
        Name = "Coding",
        BaseUrl = "https://example.test/v1",
        ApiKey = "secret",
        Protocol = BackendProtocol.ChatCompletionsImageJson,
        MainlineModel = mainlineModel,
        SupportsChat = true,
        IsEnabled = true
    };

    private static CodingAgentRequest TestRequest() => new()
    {
        Goal = "review",
        WorkspacePath = "C:/workspace",
        WorkspaceSnapshot = new CodingWorkspaceSnapshot
        {
            WorkspacePath = "C:/workspace",
            FileTree = new[] { "README.md" },
            Files = Array.Empty<CodingWorkspaceFile>(),
            SkippedFiles = Array.Empty<string>()
        },
        MaxFileChanges = 2,
        MaxCommands = 1
    };

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

    private sealed class SequenceHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses;

        public SequenceHandler(params HttpResponseMessage[] responses)
        {
            _responses = new Queue<HttpResponseMessage>(responses);
        }

        public List<string> RequestBodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestBodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken));
            return _responses.Dequeue();
        }
    }
}
