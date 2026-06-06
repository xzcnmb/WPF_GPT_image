using Gpt2Image.Core.Models;

namespace Gpt2Image.Core.Api;

public interface IImageGenerationClient
{
    Task<GenerationResult> GenerateAsync(BackendProfile profile, ImageGenerationRequest request, CancellationToken cancellationToken);
    IAsyncEnumerable<ImageStreamEvent> StreamAgentImagesAsync(BackendProfile profile, AgentRunRequest request, CancellationToken cancellationToken);
}
