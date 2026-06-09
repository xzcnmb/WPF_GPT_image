using Gpt2Image.Core.Models;

namespace Gpt2Image.Core.Api;

public interface ICodingAgentClient
{
    Task<CodingAgentResponse> GenerateProposalAsync(
        BackendProfile profile,
        CodingAgentRequest request,
        CancellationToken cancellationToken);
}
