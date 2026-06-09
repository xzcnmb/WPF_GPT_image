using Gpt2Image.Core.Models;

namespace Gpt2Image.Core.Api;

public interface IImageGenerationClient
{
    Task<GenerationResult> GenerateAsync(BackendProfile profile, ImageGenerationRequest request, CancellationToken cancellationToken);

    Task<PromptOptimizationResult> OptimizePromptAsync(BackendProfile profile, string prompt, CancellationToken cancellationToken)
        => Task.FromResult(new PromptOptimizationResult { OptimizedPrompt = prompt });

    Task<PromptOptimizationResult> OptimizeVideoPromptAsync(BackendProfile profile, string prompt, CancellationToken cancellationToken)
        => OptimizePromptAsync(profile, prompt, cancellationToken);

    Task<ChatResult> ChatAsync(BackendProfile profile, ChatRequest request, CancellationToken cancellationToken)
        => Task.FromResult(new ChatResult { Error = "当前客户端未实现聊天接口" });

    IAsyncEnumerable<ImageStreamEvent> StreamAgentImagesAsync(BackendProfile profile, AgentRunRequest request, CancellationToken cancellationToken);
}
