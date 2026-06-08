using Gpt2Image.Core.Models;

namespace Gpt2Image.Core.Api;

public interface IVideoGenerationClient
{
    Task<VideoGenerationResult> GenerateAsync(
        BackendProfile profile,
        VideoGenerationRequest request,
        CancellationToken cancellationToken);
}
